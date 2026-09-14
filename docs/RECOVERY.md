# Fix an account connection

If an account stops returning results, start with the steps below. MailMeUp can still return results from other accounts, so check which ones were searched before treating an empty list as no mail or appointments.

## Try again after a temporary error

Open **Accounts** in the Windows app and choose **Check read access** again, or repeat the search. If Google or Microsoft asks MailMeUp to wait because of request limits, let that pause pass before trying again.

## Sign in again

If access has expired or been revoked, choose **Reconnect** for that account. In the browser, pick the same account and finish sign-in. Your saved sharing choices stay in place.

From the command line, use `mailmeup accounts connect google` or `mailmeup accounts connect microsoft` and choose the same account.

After reconnecting, start a new search. Asking for the next page of an old, failed search won't bring the recovered account back into it.

## Remove an account from this device

Choose **Remove** for the account and confirm **Remove from device**. From the command line, use `mailmeup accounts list` to find its ID, then `mailmeup accounts remove <account-id>`.

This removes the local account and its saved sign-in tokens. Your mail and calendars stay as they are. To revoke the app's access too, use your Google or Microsoft account settings.

## If saved sign-in information can't be opened

Check that your operating system's credential store is available. If you've set `MAILMEUP_DATA_DIR`, make sure the command-line app and your assistant's MailMeUp process use the same folder.

Restart older MailMeUp processes after an update. Updated versions coordinate token refresh and removal; older ones don't use the same locks. A read that's already running may finish after removal, but later reads need an available account and credentials.

## What has been tested

Recovery changes passed Windows tests with made-up accounts, including failed sign-in and several processes accessing credentials. Deliberately expiring or revoking access and reconnecting real accounts still need testing. Those checks are manual, need at least two connected accounts and run only when the owner asks. See the [test record](VALIDATION.md).
