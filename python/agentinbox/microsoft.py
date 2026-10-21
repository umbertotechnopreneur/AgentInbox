"""Explicit Microsoft public-client sign-in for IMAP, POP3 and optional SMTP."""

import contextlib
import http.client
import json
import math
import os
import re
import ssl
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

from .errors import MailError, server_response


AUTHORITY = "https://login.microsoftonline.com"
RESOURCE = "https://outlook.office.com/"
MAX_RESPONSE = 65_536
TIMEOUT = 20
INCOMING_HOSTS = {
    "imap": {"outlook.office365.com", "imap-mail.outlook.com"},
    "pop3": {"outlook.office365.com", "pop-mail.outlook.com"},
}
SMTP_HOSTS = {"smtp.office365.com", "smtp-mail.outlook.com"}


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    """Never forward an authorization POST to another endpoint."""

    def redirect_request(self, request, response, code, message, headers, new_url):
        raise MailError("microsoft_redirect", "Microsoft returned an unexpected redirect. Sign-in stopped.")


def _bounded_string(value, maximum, label):
    if not isinstance(value, str) or not value or len(value) > maximum or any(ord(c) < 33 or ord(c) == 127 or 0xD800 <= ord(c) <= 0xDFFF for c in value):
        raise MailError("microsoft_response", f"Microsoft returned an invalid {label}.")
    return value


def _duration(value, maximum):
    if type(value) is not int or not 1 <= value <= maximum:
        raise MailError("microsoft_response", "Microsoft returned an invalid expiry or polling interval.")
    return value


def _guid(value):
    if not isinstance(value, str):
        raise MailError("microsoft_configuration", "The Microsoft app or organization ID is invalid. Check the app settings.")
    try:
        normalized = str(uuid.UUID(value))
    except (ValueError, AttributeError):
        raise MailError("microsoft_configuration", "The Microsoft app or organization ID is invalid. Check the app settings.") from None
    if value.lower() != normalized:
        raise MailError("microsoft_configuration", "The Microsoft app or organization ID is invalid. Check the app settings.")
    return normalized


def _flow_key(flow_id):
    if not isinstance(flow_id, str) or not re.fullmatch(r"[a-f0-9]{32}", flow_id):
        raise MailError("invalid_reference", "Invalid Microsoft sign-in reference.")
    return "signin-" + flow_id


def _verification_uri(value):
    """Accept the HTTPS sign-in address returned by Microsoft's verified endpoint."""
    _bounded_string(value, 2048, "verification address")
    try:
        parsed = urllib.parse.urlsplit(value)
        allowed = (
            parsed.scheme == "https"
            and bool(parsed.hostname)
            and parsed.port != 0
            and parsed.username is None
            and parsed.password is None
        )
    except ValueError:
        allowed = False
    if not allowed:
        raise MailError("microsoft_response", "Microsoft did not return a valid secure sign-in address. Try again.")
    return value


def _report(diagnostic, message, *secrets):
    """Send bounded, redacted details only to an explicitly requested local trace."""
    if diagnostic is not None:
        diagnostic(server_response("Microsoft", message, *secrets))


class MicrosoftAuth:
    """Manage explicit device-code flows and protected refresh-token rotation."""

    def __init__(self, store):
        self.store = store
        self._tokens = {}

    def _account(self, identity, revision=None):
        account = self.store.account(identity)
        authentication = account.get("authentication")
        if not isinstance(authentication, dict) or set(authentication) != {"method", "client_id", "tenant"} or authentication["method"] != "microsoft_oauth":
            raise MailError("microsoft_configuration", "Select Microsoft sign-in in local account setup.")
        _guid(authentication["client_id"])
        tenant = authentication["tenant"]
        if tenant not in {"common", "organizations", "consumers"}:
            _guid(tenant)
        incoming = account["incoming"]
        if incoming["protocol"] not in INCOMING_HOSTS or incoming["host"] not in INCOMING_HOSTS[incoming["protocol"]]:
            raise MailError("microsoft_configuration", "Microsoft sign-in needs a supported Microsoft incoming mail server.")
        if account["smtp"] is not None and account["smtp"]["host"] not in SMTP_HOSTS:
            raise MailError("microsoft_configuration", "Microsoft sign-in only works with Microsoft sending servers.")
        if revision is not None and account["revision"] != revision:
            raise MailError("configuration_changed", "Account settings changed. Start Microsoft sign-in again.")
        return account

    @staticmethod
    def _scopes(account):
        protocol = account["incoming"]["protocol"]
        scopes = [RESOURCE + ("IMAP.AccessAsUser.All" if protocol == "imap" else "POP.AccessAsUser.All")]
        if account["send_enabled"]:
            scopes.append(RESOURCE + "SMTP.Send")
        return " ".join(scopes + ["offline_access"])

    @staticmethod
    def _endpoint(account, operation):
        authentication = account["authentication"]
        tenant = authentication["tenant"]
        if tenant not in {"common", "organizations", "consumers"}:
            tenant = _guid(tenant)
        if operation not in {"devicecode", "token"}:
            raise MailError("microsoft_configuration", "This Microsoft sign-in step is not supported.")
        return f"{AUTHORITY}/{tenant}/oauth2/v2.0/{operation}"

    def _post(self, account, operation, fields, *, diagnostic=None):
        """POST bounded JSON requests over verified TLS, without redirects or retries."""
        endpoint = self._endpoint(account, operation)
        _report(diagnostic, f"POST {endpoint}")
        request = urllib.request.Request(
            endpoint,
            data=urllib.parse.urlencode(fields).encode("ascii"),
            headers={"Accept": "application/json", "Content-Type": "application/x-www-form-urlencoded"},
            method="POST",
        )
        opener = urllib.request.build_opener(
            _NoRedirect(), urllib.request.HTTPSHandler(context=ssl.create_default_context())
        )
        try:
            try:
                response = opener.open(request, timeout=TIMEOUT)
            except urllib.error.HTTPError as error:
                response = error
            with response:
                if response.geturl() != endpoint:
                    raise MailError("microsoft_redirect", "Microsoft returned an unexpected address during sign-in.")
                status = response.getcode()
                content = response.read(MAX_RESPONSE + 1)
            _report(diagnostic, f"HTTP {status}; received {len(content)} bytes. Response contents hidden.")
            if len(content) > MAX_RESPONSE:
                raise MailError("microsoft_response", "Microsoft's response exceeded the size limit.")
            result = json.loads(content.decode("utf-8"))
            if not isinstance(result, dict) or (status != 200 and not isinstance(result.get("error"), str)):
                raise MailError("microsoft_response", "Microsoft returned an unexpected response during sign-in.")
            if status != 200 and status not in {400, 401, 403, 429}:
                raise MailError("microsoft_unavailable", "Microsoft sign-in is temporarily unavailable. Try again later.")
            return result
        except MailError:
            raise
        except (OSError, urllib.error.URLError, http.client.HTTPException, ValueError, UnicodeError) as error:
            sensitive = [account["email"], *fields.values()]
            encoded = [urllib.parse.quote_plus(value) for value in sensitive]
            detail = server_response("Microsoft", f"{type(error).__name__}: {error}", *sensitive, *encoded)
            _report(diagnostic, "The secure request or response could not be completed.")
            raise MailError("microsoft_unavailable", "Microsoft sign-in could not complete over a verified connection. Try again later.",
                            server_response=detail) from None

    @staticmethod
    def _raise_error(response, *secrets):
        """Expose Microsoft's bounded explanation locally, without sign-in secrets."""
        error = response.get("error")
        if "error" in response and (not isinstance(error, str) or not error or len(error) > 128):
            raise MailError("microsoft_response", "Microsoft sent a reply I couldn't read. Please try signing in again.")
        if not error:
            return

        description = response.get("error_description")
        explanation = f"{error}: {description}" if isinstance(description, str) else error
        secret_fields = ("access_token", "refresh_token", "id_token", "device_code", "user_code", "client_secret")
        sensitive = [value for value in secrets if isinstance(value, str) and value]
        for field in secret_fields:
            value = response.get(field)
            if isinstance(value, str) and value:
                sensitive.append(value)
        # A provider can echo form-encoded request values in its explanation.
        encoded = [urllib.parse.quote_plus(value) for value in sensitive]
        detail = server_response("Microsoft", explanation, *sensitive, *encoded)

        if error in {"authorization_declined", "access_denied"}:
            code = "authorization_declined"
            message = "Microsoft sign-in was cancelled or blocked. For a work account, your IT team may need to allow it."
        elif error in {"expired_token", "bad_verification_code"}:
            code = "signin_expired"
            message = "This sign-in code has expired or no longer works. Sign in with Microsoft again."
        elif error in {"invalid_client", "unauthorized_client", "invalid_scope", "invalid_request"}:
            code = "microsoft_configuration"
            message = "Microsoft couldn't connect this app. Its Microsoft setup may need fixing."
            if error == "invalid_client" and isinstance(description, str) and "must be marked as 'mobile'" in description:
                message = "Microsoft sign-in has not been enabled for this app. The app owner needs to enable desktop sign-in in its Microsoft settings."
        elif error in {"invalid_grant", "interaction_required", "consent_required", "login_required"}:
            code = "microsoft_reauthentication"
            message = "Please sign in with Microsoft again. If your work account is blocked, ask your IT team for help."
        else:
            code = "microsoft_policy"
            message = "Microsoft didn't allow this connection. For a work account, ask your IT team for help."
        raise MailError(code, message, server_response=detail)

    @contextlib.contextmanager
    def _lease(self, kind, identity):
        """Serialize a single flow or refresh without holding the settings lock."""
        if kind not in {"flow", "account"} or not isinstance(identity, str) or not re.fullmatch(r"[a-f0-9]{32}", identity):
            raise MailError("invalid_reference", "Invalid Microsoft sign-in reference.")
        directory = self.store.root / "microsoft-locks"
        directory.mkdir(mode=0o700, parents=True, exist_ok=True)
        with (directory / f"{kind}-{identity}.lock").open("a+b") as stream:
            stream.seek(0, os.SEEK_END)
            if stream.tell() == 0:
                stream.write(b"\0")
                stream.flush()
            stream.seek(0)
            try:
                if os.name == "nt":
                    import msvcrt
                    msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            except OSError:
                raise MailError("signin_busy", "Microsoft sign-in is already running. Try again shortly.") from None
            try:
                yield
            finally:
                stream.seek(0)
                if os.name == "nt":
                    msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
                else:
                    fcntl.flock(stream.fileno(), fcntl.LOCK_UN)

    def _load_flow(self, flow_id):
        try:
            if self.store.cleanup_queued(_flow_key(flow_id)):
                raise MailError("signin_expired", "This Microsoft sign-in was already used or cancelled. Start again.")
            state = json.loads(self.store.vault.get(_flow_key(flow_id)))
            if not isinstance(state, dict) or set(state) != {"account_id", "revision", "device_code", "expires", "interval", "next_poll", "scope"}:
                raise ValueError()
            _flow_key(state["account_id"])
            if type(state["revision"]) is not int or state["revision"] < 1:
                raise ValueError()
            if any(type(state[field]) not in {int, float} or not math.isfinite(state[field]) for field in ("expires", "next_poll")):
                raise ValueError()
            _bounded_string(state["device_code"], 2048, "device code")
            _duration(state["interval"], 300)
            if not isinstance(state["scope"], str) or len(state["scope"]) > 512:
                raise ValueError()
            return state
        except (ValueError, KeyError, TypeError):
            raise MailError("signin_invalid", "The protected Microsoft sign-in data is invalid. Start again.") from None

    def _save_flow(self, flow_id, state):
        with self.store.locked():
            self._account(state["account_id"], state["revision"])
            self.store.vault.put(_flow_key(flow_id), json.dumps(state, separators=(",", ":")))

    def _discard(self, flow_id):
        return self.store.retire_protected([_flow_key(flow_id)])

    def start(self, account_id, *, diagnostic=None):
        """Start a user-requested Microsoft device flow without opening a browser."""
        account = self._account(account_id)
        _report(diagnostic, f"App ID: {account['authentication']['client_id']}; account type: {account['authentication']['tenant']}.")
        _report(diagnostic, f"Requested mail permissions: {self._scopes(account)}")
        started = time.time()
        response = self._post(account, "devicecode", {
            "client_id": account["authentication"]["client_id"], "scope": self._scopes(account)
        }, diagnostic=diagnostic)
        self._raise_error(response, account["email"])
        device_code = _bounded_string(response.get("device_code"), 2048, "device code")
        user_code = _bounded_string(response.get("user_code"), 32, "user code")
        if not re.fullmatch(r"[A-Za-z0-9-]{4,32}", user_code):
            raise MailError("microsoft_response", "Microsoft returned an invalid user code.")
        verification = _verification_uri(response.get("verification_uri"))
        address = urllib.parse.urlsplit(verification)
        # The address can carry a sign-in code in its query, fragment or path.
        _report(diagnostic, f"Sign-in address accepted: {address.scheme}://{address.netloc}. Path, query and fragment hidden.", device_code, user_code, account["email"])
        expires_in = _duration(response.get("expires_in"), 3600)
        interval = _duration(response.get("interval", 5), 300)
        flow_id = uuid.uuid4().hex
        state = {"account_id": account["id"], "revision": account["revision"], "device_code": device_code,
                 "expires": started + expires_in, "interval": interval,
                 "next_poll": time.time() + interval, "scope": self._scopes(account)}
        self._save_flow(flow_id, state)
        _report(diagnostic, f"Temporary sign-in saved securely. Code valid for {expires_in} seconds; checking every {interval} seconds.")
        return {"flow_id": flow_id, "user_code": user_code, "verification_uri": verification,
                "expires_in": max(1, math.floor(state["expires"] - time.time())), "interval": interval}

    def _token_response(self, response, account, require_refresh, *secrets):
        self._raise_error(response, account["email"], *secrets)
        if not isinstance(response.get("token_type"), str) or response["token_type"].lower() != "bearer":
            raise MailError("microsoft_response", "Microsoft did not finish sign-in. Please try again.")
        access = _bounded_string(response.get("access_token"), 32_768, "access token")
        expires = _duration(response.get("expires_in"), 86_400)
        supplied = response.get("scope")
        if not isinstance(supplied, str) or len(supplied) > 2048:
            raise MailError("microsoft_response", "Microsoft did not confirm the granted mail permissions.")
        granted = set()
        for scope in supplied.split():
            if scope in {"offline_access", "openid", "profile", "email"}:
                continue
            if scope.startswith(RESOURCE):
                granted.add(scope[len(RESOURCE):])
            elif scope.startswith("https://outlook.office365.com/"):
                granted.add(scope[len("https://outlook.office365.com/"):])
            else:
                raise MailError("microsoft_response", "Microsoft returned permissions for an unexpected resource.")
        required = {scope[len(RESOURCE):] for scope in self._scopes(account).split() if scope.startswith(RESOURCE)}
        if not required.issubset(granted) or not granted.issubset({"IMAP.AccessAsUser.All", "POP.AccessAsUser.All", "SMTP.Send"}):
            raise MailError("microsoft_permission", "Microsoft did not grant the required mail permissions.")
        refresh = response.get("refresh_token")
        if refresh is not None:
            _bounded_string(refresh, 2560, "refresh token")
            if len(refresh.encode("utf-8")) > 2560:
                raise MailError("credential_too_large", "The refresh token exceeds the protected credential size limit.")
        elif require_refresh:
            raise MailError("microsoft_permission", "Microsoft did not allow this app to stay signed in. Sign in again and check the permissions it asks for.")
        return access, refresh, expires

    @staticmethod
    def _cache_key(account):
        return account["id"], account["revision"], account.get("oauth_secret")

    def poll(self, flow_id, *, diagnostic=None):
        """Poll once at the persisted interval and consume successful flows once."""
        _flow_key(flow_id)
        with self._lease("flow", flow_id):
            state = self._load_flow(flow_id)
            if time.time() >= state["expires"]:
                cleanup = self._discard(flow_id)
                suffix = " Protected-data cleanup remains pending; retry cleanup locally." if cleanup["cleanup_pending"] else ""
                raise MailError("signin_expired", "Microsoft sign-in expired. Start again." + suffix)
            account = self._account(state["account_id"], state["revision"])
            if state["scope"] != self._scopes(account):
                raise MailError("configuration_changed", "Mail permissions changed. Start Microsoft sign-in again.")
            remaining = state["next_poll"] - time.time()
            if remaining > 0:
                return {"status": "pending", "interval": max(1, math.ceil(remaining))}
            with self._lease("account", account["id"]):
                started = time.time()
                state["next_poll"] = started + state["interval"]
                self._save_flow(flow_id, state)
                response = self._post(account, "token", {
                    "grant_type": "urn:ietf:params:oauth:grant-type:device_code",
                    "client_id": account["authentication"]["client_id"], "device_code": state["device_code"],
                }, diagnostic=diagnostic)
                if isinstance(response.get("error"), str) and response["error"] in {"authorization_pending", "slow_down"}:
                    _report(diagnostic, "Microsoft is waiting for you to finish sign-in." if response["error"] == "authorization_pending"
                            else "Microsoft asked the app to check less often.")
                    if response["error"] == "slow_down":
                        state["interval"] = min(300, state["interval"] + 5)
                    state["next_poll"] = time.time() + state["interval"]
                    self._save_flow(flow_id, state)
                    return {"status": "pending", "interval": state["interval"]}
                try:
                    access, refresh, lifetime = self._token_response(response, account, True, state["device_code"])
                    _report(diagnostic, "Microsoft accepted sign-in and confirmed the requested mail permissions.")
                    if time.time() >= state["expires"]:
                        raise MailError("signin_expired", "Microsoft sign-in expired. Start again.")
                    connected = self.store.connect_oauth(
                        account["id"], account["revision"], refresh,
                        consumed_references=(_flow_key(flow_id),),
                    )
                except Exception as failure:
                    error = failure if isinstance(failure, MailError) else MailError("microsoft_persistence", "Microsoft sign-in could not be saved. Start again after checking local storage.")
                    try:
                        cleanup = self._discard(flow_id)
                    except Exception:
                        raise MailError(error.code, str(error) + " Temporary sign-in data could not be removed. Try cancelling sign-in again.",
                                        server_response=error.server_response) from None
                    if cleanup["cleanup_pending"]:
                        raise MailError(error.code, str(error) + " Temporary sign-in data still needs to be removed. Use Retry cleanup.",
                                        server_response=error.server_response) from None
                    raise error from None
                # The connection and consumed-flow marker are already committed. Cache
                # population is optional and must not turn that outcome into a failure.
                try:
                    current = self._account(connected["id"], connected["revision"])
                except MailError:
                    pass
                else:
                    self._tokens[self._cache_key(current)] = (access, started + lifetime)
                return {"status": "connected", "account": connected,
                        **{key: connected[key] for key in ("cleanup_pending", "cleanup_pending_count", "cleanup_error")}}

    def cancel(self, flow_id):
        """Forget one local device flow without changing an existing connection."""
        _flow_key(flow_id)
        with self._lease("flow", flow_id):
            cleanup = self._discard(flow_id)
        return {"status": "cancelled", **cleanup}

    def access_token(self, account):
        """Refresh under a per-account lease; never persist access tokens."""
        current = self._account(account["id"], account["revision"])
        key = self._cache_key(current)
        self._tokens = {stored: value for stored, value in self._tokens.items() if value[1] > time.time()}
        cached = self._tokens.get(key)
        if cached is not None and cached[1] > time.time() + 60:
            return cached[0]
        with self._lease("account", current["id"]):
            current = self._account(current["id"], current["revision"])
            reference = current.get("oauth_secret")
            if not reference:
                raise MailError("microsoft_signin_required", "Connect this Microsoft account in local setup first.")
            refresh = self.store.vault.get(reference)
            _bounded_string(refresh, 2560, "refresh token")
            started = time.time()
            response = self._post(current, "token", {
                "grant_type": "refresh_token", "client_id": current["authentication"]["client_id"],
                "refresh_token": refresh, "scope": self._scopes(current),
            })
            access, rotated, lifetime = self._token_response(response, current, False, refresh)
            with self.store.locked():
                latest = self._account(current["id"], current["revision"])
                if latest.get("oauth_secret") != reference:
                    raise MailError("configuration_changed", "Your Microsoft sign-in details changed. Try again with the saved account.")
                if rotated is not None:
                    self.store.vault.put(reference, rotated)
                self._tokens[self._cache_key(latest)] = (access, started + lifetime)
            return access
