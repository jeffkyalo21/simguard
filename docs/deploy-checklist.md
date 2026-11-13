# Deploy checklist: carrier server and automatic deploys

Do the steps in order. Each step is a command to paste or a console click path,
with the expected result. Stop at the first result that does not match.

Where to run:

- **CloudShell**: AWS console, top bar, CloudShell icon, in the account and
  Region of the instances (it has the aws CLI and git).
- **Laptop**: Git Bash in the repository, with `gh` signed in to the repository owner account.
- **Carrier** / **SimGuard**: a shell on that instance through Session Manager:
  EC2 console, Instances, select the instance, Connect, Session Manager tab,
  Connect. Then `sudo -i` is not needed; the commands use `sudo` where needed.

Never paste a key from mock.env into a chat, a ticket or a commit. Type it only
into SimGuard Settings.

## A. Check what already exists

1. **CloudShell.** Set the values once per CloudShell session:

   ```bash
   export AWS_REGION=<REGION>
   export CARRIER_ID=<CARRIER_INSTANCE_ID>
   export SIMGUARD_ID=<SIMGUARD_INSTANCE_ID>
   export ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text); echo "$ACCOUNT_ID"
   ```

   Expected: a 12 digit account number.

2. **CloudShell.** Both instances are managed by Systems Manager:

   ```bash
   aws ssm describe-instance-information --region "$AWS_REGION" \
     --filters "Key=InstanceIds,Values=$CARRIER_ID,$SIMGUARD_ID" \
     --query 'InstanceInformationList[].[InstanceId,PingStatus,PlatformName,PlatformVersion]' --output table
   ```

   Expected: two rows, both `Online`, `Ubuntu`.

3. **CloudShell.** The carrier instance, its role and its address:

   ```bash
   aws ec2 describe-instances --region "$AWS_REGION" --instance-ids "$CARRIER_ID" \
     --query 'Reservations[0].Instances[0].[InstanceType,PublicIpAddress,IamInstanceProfile.Arn,BlockDeviceMappings[0].Ebs.VolumeId]' --output text
   aws ec2 describe-addresses --region "$AWS_REGION" --filters "Name=instance-id,Values=$CARRIER_ID" \
     --query 'Addresses[0].PublicIp' --output text
   ```

   Expected: `t3.micro`, the Elastic IP (both commands print the same address),
   an instance profile whose role has `AmazonSSMManagedInstanceCore`.

4. **CloudShell.** The carrier security group:

   ```bash
   SG=$(aws ec2 describe-instances --region "$AWS_REGION" --instance-ids "$CARRIER_ID" \
     --query 'Reservations[0].Instances[0].SecurityGroups[0].GroupId' --output text)
   aws ec2 describe-security-groups --region "$AWS_REGION" --group-ids "$SG" \
     --query 'SecurityGroups[0].IpPermissions[].[FromPort,ToPort,IpRanges[].CidrIp]' --output json
   ```

   Expected: exactly port 80 with `0.0.0.0/0` and port 443 with
   `3.78.174.212/32`. No port 22. (If the SimGuard server ever gets a new
   public address, change the 443 rule.)

5. **CloudShell.** DNS:

   ```bash
   getent hosts carrier.sim-guard.app || nslookup carrier.sim-guard.app
   ```

   Expected: the Elastic IP from step 3.

6. **CloudShell.** The GitHub OIDC provider:

   ```bash
   aws iam list-open-id-connect-providers --query 'OpenIDConnectProviderList[].Arn' --output text | tr '\t' '\n' | grep token.actions
   ```

   Expected: `arn:aws:iam::<account>:oidc-provider/token.actions.githubusercontent.com`.

7. **Laptop.** GitHub environments and variables:

   ```bash
   gh api repos/jeffkyalo21/simguard/environments --jq '.environments[] | [.name, ([.protection_rules[].type] | join(","))] | @tsv'
   gh variable list --repo jeffkyalo21/simguard
   ```

   Expected: environments `carrier` and `production` (production shows
   `required_reviewers`); variables `AWS_REGION`, `CARRIER_INSTANCE_ID`,
   `SIMGUARD_INSTANCE_ID`.

8. **GitHub.** Restrict both environments to `main`: repository, Settings,
   Environments, `carrier`, Deployment branches and tags, Selected branches and
   tags, Add deployment branch rule, `main`, Add rule. Same for `production`.
   In `production` check that Required reviewers lists you and that "Prevent
   self-review" is off (otherwise you cannot approve your own run).
   Expected: both environments show "1 branch rule".

## B. Create the two deploy roles

9. **CloudShell.** Get the repository and fill in the placeholders in the
   policy files (only in this CloudShell copy):

   ```bash
   git clone --depth 1 https://github.com/jeffkyalo21/simguard.git ~/simguard-policies 2>/dev/null || git -C ~/simguard-policies pull
   cd ~/simguard-policies/deploy/aws
   mkdir -p ~/policies
   for f in *-policy.json; do
     sed -e "s/<ACCOUNT_ID>/$ACCOUNT_ID/g" -e "s/<REGION>/$AWS_REGION/g" \
         -e "s/<CARRIER_INSTANCE_ID>/$CARRIER_ID/g" -e "s/<SIMGUARD_INSTANCE_ID>/$SIMGUARD_ID/g" "$f" > ~/policies/"$f"
   done
   grep -L '<' ~/policies/*.json | wc -l
   ```

   Expected: `4` (no placeholder left in any of the four files).

10. **CloudShell.** Create the roles:

    ```bash
    cd ~/policies
    for r in carrier simguard; do
      aws iam create-role --role-name "github-deploy-$r" \
        --assume-role-policy-document "file://$r-deploy-trust-policy.json" \
        --max-session-duration 3600 --query Role.Arn --output text
      aws iam put-role-policy --role-name "github-deploy-$r" --policy-name ssm-deploy \
        --policy-document "file://$r-deploy-permissions-policy.json"
    done
    ```

    Expected: two ARNs, `arn:aws:iam::<account>:role/github-deploy-carrier`
    and `.../github-deploy-simguard`, and no error. Each role can only send
    `AWS-RunShellScript` to its own instance and read the results.

11. **Laptop.** Make GitHub's token name the environment and the branch, which
    the trust policies check (`repo:jeffkyalo21/simguard:environment:<env>:ref:refs/heads/main`):

    ```bash
    gh api -X PUT repos/jeffkyalo21/simguard/actions/oidc/customization/sub \
      --input - <<< '{"use_default": false, "include_claim_keys": ["repo", "context", "ref"]}'
    gh api repos/jeffkyalo21/simguard/actions/oidc/customization/sub
    ```

    Expected: `{"use_default":false,"include_claim_keys":["repo","context","ref"]}`.
    This changes the token of every workflow in this repository; no other
    workflow uses AWS today. To undo: the same PUT with `{"use_default": true}`.

12. **Laptop.** Add the two role ARNs as repository variables:

    ```bash
    gh variable set CARRIER_DEPLOY_ROLE_ARN  --repo jeffkyalo21/simguard --body "arn:aws:iam::<ACCOUNT_ID>:role/github-deploy-carrier"
    gh variable set SIMGUARD_DEPLOY_ROLE_ARN --repo jeffkyalo21/simguard --body "arn:aws:iam::<ACCOUNT_ID>:role/github-deploy-simguard"
    gh variable list --repo jeffkyalo21/simguard
    ```

    Expected: five variables: `AWS_REGION`, `CARRIER_INSTANCE_ID`,
    `SIMGUARD_INSTANCE_ID`, `CARRIER_DEPLOY_ROLE_ARN`, `SIMGUARD_DEPLOY_ROLE_ARN`.

## C. Set up the carrier server

13. **Carrier.** Run the setup script (installs Docker, git, git-lfs, python3,
    clones the repository, creates the folders; adds swap only below 768 MiB):

    ```bash
    curl -fsSL https://raw.githubusercontent.com/jeffkyalo21/simguard/main/deploy/aws/carrier/setup.sh -o /tmp/setup.sh
    sudo bash /tmp/setup.sh
    ```

    Expected, at the end: `Docker Compose version v2...`, `no swap needed (9xx MiB RAM)`,
    `clone at /opt/simguard-carrier, commit <hash>`, `Setup finished.`
    (This needs the carrier work merged into `main` first.)

14. **Carrier.** Build the data folder on the host:

    ```bash
    sudo /opt/simguard-carrier/deploy/carrier/scripts/build-mock-data.sh
    ```

    Expected: `Mock data in /srv/carrier/mock-data: 700 SIMs, 63000 usage rows, 90 days.`

15. **Carrier.** Create mock.env on the host with new keys (never copied from
    the repository, a laptop or the SimGuard server):

    ```bash
    sudo /opt/simguard-carrier/deploy/carrier/scripts/create-mock-env.sh
    sudo stat -c '%a %U %n' /srv/carrier/mock.env
    ```

    Expected: `Wrote /srv/carrier/mock.env (mode 600) with keys for: Airtel Safaricom Telkom and MOCK_ADMIN_KEY. No key was printed.`
    and `600 root /srv/carrier/mock.env`.

## D. First deploy and checks

16. **Laptop.** Run the carrier deploy once:

    ```bash
    gh workflow run deploy-carrier.yml --repo jeffkyalo21/simguard --ref main
    sleep 5; gh run watch --repo jeffkyalo21/simguard "$(gh run list --repo jeffkyalo21/simguard --workflow deploy-carrier.yml --limit 1 --json databaseId --jq '.[0].databaseId')"
    ```

    Expected: the run ends green; its log shows `health: mock healthy`,
    `health: https://carrier.sim-guard.app/ through Caddy answered 200` and
    `deploy finished`. The first run builds the image (a few minutes).
    If it fails at "AWS credentials": recheck steps 8, 10 and 11.

17. **Carrier.** Status of the release pointer:

    ```bash
    sudo /opt/simguard-carrier/deploy/carrier/scripts/status.sh
    ```

    Expected: `"status": "releasing"`, `"releasedThroughDay": 80`,
    `"releasedThroughDate": "2025-03-21"`, `"daysLeft": 10`, `"nextReleaseAt"` the next 01:00 `+03:00`.

18. **SimGuard.** The feed is reachable from the SimGuard server only:

    ```bash
    curl -sS https://carrier.sim-guard.app/; echo
    curl -sS -o /dev/null -w '%{http_code}\n' 'https://carrier.sim-guard.app/v1/usage/summary?date=2025-01-01'
    curl -sS -o /dev/null -w '%{http_code}\n' https://carrier.sim-guard.app/admin/status
    ```

    Expected: `{"name":"SimGuard carrier mock","sims":700,...,"status":"releasing"}`,
    then `401` (key required), then `404` (admin blocked from outside).
    From a laptop the same `curl` times out (443 is open to the SimGuard server only).

19. **SimGuard web app**, as the Security Officer: Settings, Carriers, Usage
    feed, for each carrier (Safaricom, Airtel, Telkom):
    1. On the **Carrier**, read that carrier's key:
       `sudo grep '^CarrierMock__Keys__Safaricom=' /srv/carrier/mock.env | cut -d= -f2`
    2. In SimGuard set Address `https://carrier.sim-guard.app`, type the key
       into API key (the old key does not work here), Save, then Test.
       Expected: Test succeeds.
    3. Sync now. Expected: "Caught up: the carrier has no data for 2025-03-22 yet."
       (SimGuard already has the days up to 2025-03-21 from the import.)

20. **Carrier, then SimGuard web app.** One release end to end:
    `sudo /opt/simguard-carrier/deploy/carrier/scripts/release-next-day.sh`
    (expected `"status": "released"`, `"releasedThroughDate": "2025-03-22"`),
    then Sync now for one carrier. Expected: one day imported, and a scoring
    run with trigger `import` shortly after.

## E. The production deploy and its approval gate

Do this at a quiet time: a real run backs up the database and restarts the
backend, ML service and web app (about a minute of errors in the web app).

21. **SimGuard.** Check the live layout the workflow expects:

    ```bash
    cd /opt/simguard && git log -1 --format='%h %s' && git status --short | head
    sudo docker compose -f deploy/docker-compose.yml ps --format '{{.Service}} {{.Status}}'
    ```

    Expected: the checkout is at `/opt/simguard`; services include `backend`,
    `ml`, `frontend`, `postgres`, `caddy`, `carriermock`, all `Up ... (healthy)`.
    `git status` may list changes under `deploy/` (the pinned layout); it must
    not list `backend/`, `frontend/` or `ml-service/`.

22. **Laptop.** Start the production deploy by hand:

    ```bash
    gh workflow run deploy-simguard.yml --repo jeffkyalo21/simguard --ref main
    ```

    Expected: the run shows "Waiting" with "Review deployments" for
    `production`; nothing happens on the server yet. GitHub, Actions, the run,
    Review deployments, tick production, Approve and deploy.

23. **Laptop.** Watch it:

    ```bash
    gh run watch --repo jeffkyalo21/simguard "$(gh run list --repo jeffkyalo21/simguard --workflow deploy-simguard.yml --limit 1 --json databaseId --jq '.[0].databaseId')"
    ```

    Expected, in the log: `backup verified: /backups/<db>_<time>.dump (...)`,
    `building backend`, `building ml`, `building frontend`, `check: backend /health 200`,
    `check: ML /health 200`, `check: https://sim-guard.app/ 200`, `deploy finished`.
    If main has no application changes compared with the server, it ends early
    with `Nothing to deploy`; run it again with the `force` input ticked to go
    through the whole path once. The log also says whether
    `deploy/docker-compose.yml` on main differs from the server's (ignored).

24. From now on: a push to `main` that changes `deploy/carrier/**` or the mock
    deploys the carrier at once; a push that changes `backend/**`,
    `frontend/**` or `ml-service/**` waits for your approval in `production`.
    The mock lives under `backend/`, so a mock change also asks for a production
    approval: reject it if only the mock changed.

## Later

- The SimGuard instance role (today `AmazonSSMManagedInstanceCore`) will later
  also get a Secrets Manager policy, when secrets move out of `deploy/.env`.
  That does not change these roles or workflows.
- Rollback and troubleshooting: [deployment.md](deployment.md#automatic-deploys-from-github).
