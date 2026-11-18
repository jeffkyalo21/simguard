# SimGuard user guide

This guide is for the people who use the SimGuard website. It needs no
technical knowledge. Screen and button names are written the way they appear in
the app. If you run or change SimGuard itself, read the
[developer guide](developer-guide.md) instead.

Contents:

1. [What SimGuard does](#1-what-simguard-does)
2. [The three roles](#2-the-three-roles)
3. [Getting an account and signing in](#3-getting-an-account-and-signing-in)
4. [Forgot password](#4-forgot-password)
5. [The dashboard](#5-the-dashboard)
6. [Importing data and the carrier usage feed](#6-importing-data-and-the-carrier-usage-feed)
7. [SIMs and allocations](#7-sims-and-allocations)
8. [Alerts](#8-alerts)
9. [Tier recommendations](#9-tier-recommendations)
10. [Risk scores and reasons](#10-risk-scores-and-reasons)
11. [Usage](#11-usage)
12. [The audit log](#12-the-audit-log)
13. [Your profile](#13-your-profile)
14. [Admin tasks](#14-admin-tasks)
15. [Troubleshooting](#15-troubleshooting)
16. [Glossary](#16-glossary)
17. [Screenshots still to add](#17-screenshots-still-to-add)

## 1. What SimGuard does

Companies that give SIM cards to staff lose money when a SIM is lost, used in
another device, used for far more data than it should, or paid for and never
used. SimGuard keeps the list of SIMs, employees, departments and allocations,
loads the daily usage of every SIM, gives each SIM a risk score every day, and
raises an alert when a SIM keeps looking unusual. A person then reviews the
alert and decides what to do. SimGuard never suspends a SIM by itself. It also
suggests a bigger or smaller data bundle (a tier) when a SIM is over or under
provisioned, and it records every change in an audit log.

SimGuard can be wrong. Some alerts will be false, and some misuse will be
missed. That is why a person reviews every alert. The models were tested on
synthetic data, not on a real fleet (see the known limitations in the
[README](../README.md#known-limitations)).

## 2. The three roles

Your Admin gives you one role. The menu at the top shows only the pages your
role may open, and buttons your role cannot use are switched off with a short
reason.

| Role | What you can do | What you cannot do |
| ---- | --------------- | ------------------ |
| Admin | Create and manage user accounts (invite, change role, unlock, reset two-step sign-in, disable), approve or decline access requests, see system health, read the system part of the audit log, change your own profile | See or change the organisation's data: no dashboard, SIMs, alerts, usage, allocations or imports |
| Security Officer | Run the organisation: departments, employees, SIMs, carriers and usage feeds, allocations, imports, tier changes, alert reviews, tier recommendations, refreshing scores. Can read everything the Auditor can, except the audit log | Manage user accounts. Open the audit log |
| Auditor | Read only: dashboard, SIMs, alerts, usage, allocations, carriers, tier recommendations and the audit log | Change anything. Buttons show "Auditors have read-only access" |

After signing in, an Admin lands on the Admin page. The other roles land on
the Dashboard. If you open a page that is not part of your role, SimGuard says
"You do not have access to this page" and offers "Go to your home page".

The full list of what each role may do on each endpoint is in
[backend/docs/auth.md](../backend/docs/auth.md).

## 3. Getting an account and signing in

### Asking for an account

On the SimGuard home page choose **Register**. Enter your name, work email,
organisation and the access you need (Security Officer or Auditor), add a note
if you like, and choose **Send request**. An Admin reviews it. When they
approve, SimGuard emails you a sign-in link and a one-time password. If the
Admin invites you directly, you get the same email.

### First sign-in

1. Open the link, enter your email and the one-time password, and choose
   **Sign in**.
2. SimGuard asks for a new password ("Choose a new password"). The new password
   needs at least 12 characters, at least 4 different characters, and must
   differ from the current one.
3. SimGuard asks you to **Set up two-step sign-in**. Scan the QR code with an
   authenticator app on your phone. If you cannot scan it, enter the setup key
   shown below the code (use **Copy key**). Then enter the 6-digit code the app
   shows and choose **Confirm code**.
4. SimGuard shows your **recovery codes**. Save them now, using **Copy codes**
   or **Download**. Each code works once, and they are shown only this one time.
   Tick **I have saved these** to continue.

Two-step sign-in cannot be turned off.

### Every later sign-in

Enter your email and password, then the 6-digit code from your authenticator
app. If you lost your phone, choose **Use a recovery code instead** and enter
one of your saved codes (for example `K7M2Q-9XW4P`). Each recovery code works
once. You can make 10 new codes later (see [Your profile](#13-your-profile)),
and an Admin can reset your two-step sign-in if you lose both the phone and
the codes.

After 5 wrong tries (password, code or current password) sign-in locks for 15
minutes. A locked or disabled account shows the same "Email or password is
incorrect." message, so ask an Admin to check it.

## 4. Forgot password

1. On the sign-in page choose **Forgot password?**.
2. Enter your email and choose **Send link**. SimGuard always answers "If that
   account exists, we sent a link." so nobody can use the page to find out who
   has an account.
3. Open the email (check the spam folder too, it can take a few minutes) and
   follow the link. The link works for 30 minutes and only once. A newer
   request replaces an older link.
4. Choose a new password (same rules as above) and choose **Change password**.
   Two-step sign-in stays on, so you still need your authenticator code.

If you see "This link is invalid or has expired", choose **Get a new link**.
A password reset does not unlock a locked account. Wait for the 15 minutes to
pass or ask an Admin to unlock it. If email is not set up on your SimGuard,
the Admin can tell you in the System health page; then ask the Admin to resend
your sign-in details another way.

## 5. The dashboard

The Dashboard is the first page for a Security Officer and an Auditor. It shows
the state of the whole fleet.

Choose a **Period** in the corner: Last 7 days, Last 30 days or Last 90 days.
A note under the page says "Data up to <date>, the latest usage day." The
period ends on that day, not on today's date.

| Tile or card | What it shows |
| ------------ | ------------- |
| Active SIMs | SIMs whose status is not Inactive |
| Flagged | Alerts that are Flagged right now, whatever their age |
| To review | Alerts that are Escalated right now, whatever their age |
| Resolved | Alerts in the period that are now Cleared |
| Data wasted | Paid data against used data in the period, with the share wasted (for example "24% of paid data"). "Most unused by department" lists the departments with the most unused data |
| Alerts by type | Alerts in the period, counted as Inactive billing, Sharing, Reselling, Ghost usage or Other |
| Needs review | The 5 most recent Flagged or Escalated alerts. Choose one to open it |
| Tier changes | Open tier recommendations, counted as Upgrade and Downgrade |

Below the period you see "Scores are up to <date>". A Security Officer also
has a **Refresh scores** button that starts a scoring run at once. While it
runs the button says "Refreshing scores". If a run is already waiting, your
request joins it. If the run fails, SimGuard says "The last scoring run did
not finish. Try again, or ask the SimGuard host to check the ML service."

A new organisation first sees **Set up your organisation**, a checklist:
Carriers, Departments, Employees, SIMs, Allocations, Daily usage and Risk
scores. Each step has an Import, Add or View button. Risk scores appear after
the first usage import. Once it says "Setup complete" you can **Dismiss** it
(this is remembered in your browser only).

## 6. Importing data and the carrier usage feed

Only a Security Officer can import data or manage the usage feed. The
Auditor can read the feed status.

### The five CSV files

Open **Imports**. Import the files in this order, because each one needs the
one before it:

1. **Departments**: one department name per row.
2. **Employees**: needs departments first.
3. **SIMs**: needs carriers first. Add carriers in **Settings**, then **Carriers**.
4. **Allocations**: needs SIMs and employees first.
5. **Daily usage**: needs SIMs first. Scoring needs 15 days of usage per SIM.

For each file:

1. Choose the **Data type**.
2. Choose **Download template** to get a CSV with the exact columns and two
   sample rows. Keep the header as it is.
3. Drag your CSV onto the page or choose **Choose file**. Only `.csv` files up
   to 25 MB are accepted.
4. Choose **Preview**. Nothing is saved yet. The preview shows the totals (rows,
   valid, with errors, to add, to update, unchanged), the row errors with row
   and column, and warnings. For usage files it also shows the coverage: how
   many SIMs are ready for scoring and how many have too little history or gaps.
5. Fix the errors in your file and preview again, or tick **Import valid rows
   only** (rows with errors are left out). Then choose **Confirm import**.
6. SimGuard says "Import complete" and "Scoring starts automatically." after a
   usage import.

If you change the file after previewing, SimGuard asks you to preview again.
The **History** tab lists every import with its type, file, who uploaded it,
status (Committed, Rejected or Failed) and row counts. Imports made by the
carrier feed appear there as "Carrier feed: <carrier>".

The column rules are in [backend/docs/imports.md](../backend/docs/imports.md).

### The carrier usage feed

Instead of uploading usage files every day, a Security Officer can connect a
carrier so SimGuard pulls the usage by itself. The contract is in
[carrier-feed.md](carrier-feed.md).

1. Open **Settings**, then the **Carriers** tab. Add the carrier if it is not
   there (**Add carrier**).
2. In the carrier's row choose **Usage feed**.
3. Enter the **Feed address** (it must start with `https://`) and the **API
   key** the carrier gave you. The key is stored encrypted and is never shown
   again. Choose **Replace key** to change it later.
4. Choose a **Start date**: the first day to pull.
5. Choose **Test connection**. You should see "Connection works".
6. Tick **Pull usage every day** and choose **Save feed**.
7. Choose **Sync now** to pull straight away. Save your changes first,
   otherwise SimGuard says "Save your changes before syncing."

Every enabled feed is also synced once a day at 01:30 Nairobi time, and the
daily scoring run follows at 02:00. The feed card shows its status (Not
connected, Saved, switched off, On, Syncing now, Last sync worked, Last sync
failed), the day usage was loaded up to, and when the last sync ran. If the
carrier has no new day yet, Sync now ends with "Caught up" and changes
nothing. In this project the carrier is a demo mock that replays synthetic
usage one more day every night (see [carrier-server.md](carrier-server.md)).

## 7. SIMs and allocations

### The SIM list and a SIM's page

**SIMs** lists every SIM with its number, employee, department, tier, data used
and status. Use **Search SIMs** (number or ICCID) and the Department and
Operator filters. The search box at the top of every page also finds SIMs by
number, ICCID or employee; press `/` to jump to it.

Choose a SIM to open **SIM details**: its details, the data used this cycle
against its limit, and the tabs **Alerts**, **Allocations** and **Activity**.
A Security Officer can edit the SIM and use **Change tier**.

A SIM's **Status** (Assigned, Available or Inactive) cannot be edited by hand.
It comes from your carrier data, imports and allocations ("Status comes from
your carrier data.").

### Adding a SIM

A Security Officer chooses **Add SIM** and fills in:

- **Number**: a valid Kenyan number, for example 0712 345 678.
- **ICCID**: the long number printed on the SIM card. Type it or scan it.
- **Operator**: choose a carrier. If there are none, choose **Add a carrier**.
- **Plan** and **Tier**: choose the tier (Support, Professional, Manager or Field).
- **Monthly cost (KES)**: 0 uses the tier's monthly cost.

SimGuard refuses a number or ICCID that already exists. If the ICCID is
already registered, the form offers a link to that SIM.

### Scanning a SIM with the camera

Both the **Add SIM** form and the **New allocation** form have a **Scan**
button.

1. Choose **Scan SIM**. The browser asks once for camera access. Allow it.
2. Hold the barcode on the SIM card in front of the camera. The status line says
   "Looking for a barcode".
3. If the card has no barcode, choose **Read the printed number**. SimGuard
   reads the digits printed on the card. This can take a few seconds.
4. You can also choose **Upload a photo instead**.

The picture stays on your device. Nothing is uploaded or saved. A scan only
fills in the field: nothing saves until you save the form. A valid ICCID has
19 or 20 digits and starts with 89. If the number is not valid you see "That
does not look like a SIM number, try again or type it."

The camera needs a secure (https) page. The first time you use the printed
number reading, your browser downloads a small reading model from the internet.

### Allocating a SIM

**Allocations** lists who holds which SIM, with Active and Ended filters.
A Security Officer chooses **New allocation**:

1. **SIM**: choose an available SIM, or choose **Scan SIM**. If the scanned SIM
   is already allocated, SimGuard tells you who has it. If it is not
   registered, choose **Register this SIM**.
2. **Employee**: type at least 2 letters of the name or email and pick the
   person. A summary shows their name, email, department and job role. Choose
   **Choose someone else** to change.
3. If the person is not listed, choose **Add new employee**, enter name, email,
   department and job role, and choose **Add**. If the email already exists,
   SimGuard offers **Use that employee**.
4. **Handed over by**: find the employee who hands the SIM over. The allocation
   starts now.
5. Choose **Allocate**.

To take a SIM back, choose **End** on the allocation and confirm **End
allocation**. The SIM can then be allocated again.

## 8. Alerts

### How an alert is raised

Every day each SIM gets a risk score (see [Risk scores](#10-risk-scores-and-reasons)).
An alert is raised when the pattern is sustained: at least 3 of the SIM's last
5 scored days are not cleared, and at least 1 of those days is flagged. A SIM
has at most one open alert. If it keeps misbehaving, the existing alert is
updated instead of a new one being created. After an alert is cleared as a
false positive, SimGuard raises no new alert for that SIM for 7 days.

Each alert has a type: Inactive billing, Sharing, Reselling, Ghost usage or
Other.

### Reviewing an alert

Open **Alerts**. Filter by **status**, **type** and **minimum score**, and sort
by **Newest first** or **Highest risk**. Choose an alert to open **Alert
details**:

- **Why it was flagged**: the top reasons.
- **SIM and employee**: who holds the SIM, department, SIM status, ICCID and
  operator.
- **Risk over time**, **Other recent alerts** and an **Activity timeline**.

A Security Officer then decides:

| Action | When | Result |
| ------ | ---- | ------ |
| **Confirm as flagged** | The alert is Escalated and you agree it is a problem | Status becomes Flagged |
| **Mark as confirmed misuse** | The SIM was misused | Alert is Cleared as confirmed misuse |
| **Mark as false positive** | Nothing is wrong | Alert is Cleared as a false positive |
| **Reopen** | A Cleared alert needs another look | Status goes back to Escalated |

When you clear an alert you can write a **Resolution note** (up to 500
characters), for example the suspension reference from your carrier. Every
change is written to the audit log. If someone else changed the alert first,
SimGuard says so and shows its latest status.

SimGuard only advises. To suspend or change a SIM you still contact the carrier.

### The three statuses

- **Flagged**: the pattern is strong. Review it first.
- **Escalated**: a person must look at it ("To review" on the dashboard).
- **Cleared**: a person closed it as confirmed misuse or false positive.

## 9. Tier recommendations

**Tier recommendations** lists forecasts that suggest a SIM needs a bigger or
smaller bundle: an **Upgrade** or a **Downgrade**, with the reason, for example
"Forecast: 2.4 GB over the Support bundle." Filter by **Open**, **Applied** or
**Dismissed**.

A Security Officer can:

- **Apply** a recommendation: the SIM's tier changes in SimGuard and the change
  is recorded. This does not change the bundle at the carrier, so ask your
  carrier to change it too.
- **Dismiss** it: the SIM keeps its tier and the same change is not suggested
  again for 90 days.
- **Run now** to start a new review. Runs must be at least 90 days apart (the
  Security Officer can set this between 90 and 365 days). The page says when the
  next run is allowed. SimGuard also runs a review by itself when one is due.

The Auditor can read the recommendations but cannot act on them.

## 10. Risk scores and reasons

Each SIM gets a risk score for each day it has 15 days of usage in a row. The
score is a probability from 0 to 100 percent that the day looks like misuse.
SimGuard puts each score in one of three bands:

- **Cleared**: below 10 percent.
- **Escalated**: from 10 to 90 percent. A person should look.
- **Flagged**: above 90 percent.

The score comes from four checks: unusual behaviour of the SIM itself, a
comparison with other SIMs on the same tier, a risk model that decides the band,
and a usage forecast that sizes the plan.

Every alert lists its **top reasons**, so you can check it before acting. Each
reason is a plain sentence such as "Share of sessions on hotspot" or "Data use
versus similar SIMs", with how much of the effect it explains and whether it
**Raises risk** or **Lowers risk**.

Scoring runs every day at 02:00 Nairobi time, after every usage import, and when
a Security Officer chooses **Refresh scores**. Pages read stored results, so
they keep working even when the scoring service is down. In that case the
scores are simply not refreshed.

## 11. Usage

**Usage** (Security Officer and Auditor) shows data use. Choose a **Period**,
**Department** and **Tier**. You see data used, data paid, utilisation and
unused data, the daily data use, usage by department and by tier, the **Top
SIMs by data use** and the **Least-used paid SIMs**. Those last two help find
SIMs that are paid for but not used.

## 12. The audit log

**Audit log** records who changed what and when: SIMs, allocations, alerts,
carriers, departments, employees, imports, tier changes, scoring runs and user
account changes.

- An **Auditor** sees every entry.
- An **Admin** sees the system entries only, such as user account changes and
  scoring runs.
- A Security Officer cannot open the audit log.

Search by the full **user email**, or filter by **Action** and by **From** and
**To** dates (the start date must be on or before the end date). Choose **Show
changes** on a row to see the fields that changed, with their **Before** and
**After** values. Times are shown in Nairobi time.

## 13. Your profile

Every role can open **Settings**, then **Profile**:

- **Your details**: change your name. Your email and role are managed by an
  Admin.
- **Password**: change the password you sign in with. You stay signed in.
- **Two-factor authentication**: always on. Choose **Make new recovery codes**
  to replace your 10 recovery codes. Enter your password and a current code
  from your authenticator app. The old codes stop working. Save the new ones
  straight away.
- **Delete my account**: removes your access and your personal details. This
  cannot be undone. The audit log keeps its entries but shows only your role and
  an account number. An Admin cannot delete their own account here, so there is
  always one active Admin.

## 14. Admin tasks

Admins use the **Admin** page, which has three tabs.

### System health

Shows the state of each part and refreshes every 30 seconds: **API**,
**Database**, **ML service**, **Email** and **Scoring**, with the states
Working, Needs attention or Down. If the ML service is down, scoring cannot run.
Ask whoever hosts SimGuard to restart it. If Email says "Not set up", sign-in
emails are not sent, so share sign-in details another way. Charts show users by
role, sign-ins per day and scoring runs per day for the last 14 days.

### Requests

People who used **Register** on the home page appear here as Pending. Choose
**Approve** to create the account and email the person their sign-in link and a
one-time password (you never see the password), or **Decline** to refuse. Neither
can be undone. If the email cannot be sent, the message tells you to open Users
and choose **Resend sign-in email**.

### Users

Lists everyone who can sign in, with role, status (Active, Disabled, Locked
until <time>, Has not signed in yet) and whether two-step sign-in is set up.

- **Invite user**: enter name, email and role, and choose **Send invitation**.
  The person chooses their own password the first time they sign in.
- **Change role**.
- **Resend sign-in email**: sends a new one-time password. The old password stops
  working.
- **Unlock**: clears a lockout.
- **Reset two-step sign-in**: for a person who lost their phone and recovery
  codes. They are signed out and set up two-step sign-in again at their next
  sign-in.
- **Disable** and **Enable**: a disabled account is signed out at once and cannot
  sign in.

You cannot do these things to your own account or to the last active Admin. Ask
another Admin.

If no Admin can sign in at all, the person who hosts SimGuard can recover access.
That is described in [deployment.md](deployment.md#i-am-locked-out-as-admin).

## 15. Troubleshooting

| Problem | What to do |
| ------- | ---------- |
| The camera is blocked ("The camera is blocked...") | Allow camera access for the site in your browser settings, then try again. Or choose **Upload a photo instead**, or type the number |
| "No camera found", or the camera works only on a secure page | Use **Upload a photo instead**. The camera needs https, or `localhost` |
| The scan says "That does not look like a SIM number" | Try a sharper photo or type the ICCID. It must be 19 or 20 digits starting with 89 |
| The reset link says it is invalid or has expired | Links last 30 minutes and work once. Choose **Get a new link** |
| No email arrived | Wait a few minutes and check spam. If it still does not come, ask an Admin to check System health, Email |
| "Email or password is incorrect." although you are sure | The account may be locked (5 failed tries, then 15 minutes) or disabled. Wait, or ask an Admin to unlock or enable it |
| The authenticator code does not work | Check the time on your phone. Use a recovery code. If you have none, ask an Admin to reset your two-step sign-in |
| "Too many attempts" | Wait a minute and try again |
| "Your session expired, please sign in again" | Sign in again |
| No alerts for a period | Alerts need a sustained pattern: 3 of the last 5 scored days not cleared and at least 1 flagged, so a quiet fleet or a short history gives none. Check that usage is loaded and scored (Dashboard: "Scores are up to <date>"), that SIMs have 15 days of usage, and that the period is long enough. A SIM cleared as a false positive raises no new alert for 7 days |
| "No scores yet" | Import usage or wait for the daily run. Scoring needs 15 days of usage per SIM |
| An import is blocked | Fix the row errors shown, or tick **Import valid rows only** |
| Sync now says "Save the feed address and key before syncing" | Save the usage feed first |
| A page says "Your role cannot do this" | That action belongs to another role. See [the three roles](#2-the-three-roles) |

## 16. Glossary

- **SIM**: the SIM card a company gives to an employee.
- **ICCID**: the long number printed on a SIM card that identifies the card.
  Valid ones have 19 or 20 digits and start with 89.
- **Operator or carrier**: the mobile network company that provides the SIM.
- **Tier**: the data bundle a SIM is on (Support, Professional, Manager or
  Field), with a monthly data allowance and cost.
- **Allocation**: the period in which a SIM is held by an employee.
- **Risk score**: the daily probability, from 0 to 100 percent, that a SIM's day
  looks like misuse.
- **Flagged**: an alert with a strong risk pattern, to be reviewed first.
- **Escalated**: an alert that needs a person to look at it ("To review").
- **Cleared**: an alert a person has closed as confirmed misuse or false
  positive.
- **Cohort**: the group of SIMs on the same tier that one SIM is compared with.
- **Ghost usage**: a SIM that should be idle keeps using data.
- **Sharing**: one SIM is used by more people or devices than it should be.
- **Reselling**: heavy data use that looks like the bundle is being sold on.
- **Inactive billing**: the company keeps paying for a SIM nobody uses.
- **MFA or two-step sign-in**: a 6-digit code from an authenticator app, asked
  for at every sign-in.
- **Recovery code**: a one-time code that replaces the authenticator code when
  you lose your phone.

## 17. Screenshots still to add

No images are included yet. Screenshot needed:

- Screenshot needed: the home page with the Register dialog.
- Screenshot needed: the sign-in page, then the two-step code page.
- Screenshot needed: Set up two-step sign-in with the QR code, and the recovery codes screen.
- Screenshot needed: Forgot password and Choose a new password.
- Screenshot needed: the Dashboard with the period selector and the setup checklist.
- Screenshot needed: Imports with a preview showing row errors.
- Screenshot needed: Settings, Carriers, and the Usage feed dialog.
- Screenshot needed: the SIM list and a SIM details page.
- Screenshot needed: the Scan SIM dialog.
- Screenshot needed: the New allocation form with the employee search.
- Screenshot needed: the Alerts list with filters, and an Alert details page with the review actions.
- Screenshot needed: Tier recommendations.
- Screenshot needed: Usage.
- Screenshot needed: the Audit log with a row's changes shown.
- Screenshot needed: the Admin page, tabs System health, Requests and Users.
