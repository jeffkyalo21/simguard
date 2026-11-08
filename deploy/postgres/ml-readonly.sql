-- Read-only role for the ML service. Safe to run again.
--
-- The ML service reads only the view ml_daily_usage (ml-service/app/readers/
-- postgres_reader.py, VIEW). The view is owned by the database owner, so the
-- role needs SELECT on the view and nothing on the tables behind it.
--
-- Run it AFTER the EF migrations, and again after every update that adds a
-- migration: some migrations drop and recreate the view, which loses grants.
-- deploy/scripts/apply-ml-grants.sh does this. Inputs (psql variables are
-- read from the environment so the password never appears in a command line):
--   ML_USER      role name
--   ML_PASSWORD  its password
\set ON_ERROR_STOP on
\set ml_user `printenv ML_USER`
\set ml_password `printenv ML_PASSWORD`

SELECT format('CREATE ROLE %I LOGIN', :'ml_user')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'ml_user')
\gexec

SELECT format('ALTER ROLE %I WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS', :'ml_user', :'ml_password')
\gexec

-- A second line of defence: every transaction of this role is read only.
SELECT format('ALTER ROLE %I SET default_transaction_read_only = on', :'ml_user')
\gexec

-- Start from nothing.
SELECT format('REVOKE ALL ON ALL TABLES IN SCHEMA public FROM %I', :'ml_user') \gexec
SELECT format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM %I', :'ml_user') \gexec
SELECT format('REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public FROM %I', :'ml_user') \gexec
SELECT format('REVOKE ALL ON SCHEMA public FROM %I', :'ml_user') \gexec
SELECT format('REVOKE ALL ON DATABASE %I FROM %I', current_database(), :'ml_user') \gexec

-- Then only what the reader needs.
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), :'ml_user') \gexec
SELECT format('GRANT USAGE ON SCHEMA public TO %I', :'ml_user') \gexec
SELECT format('GRANT SELECT ON TABLE public.ml_daily_usage TO %I', :'ml_user') \gexec
