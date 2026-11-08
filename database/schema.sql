-- SimGuard Database Schema
-- Excludes: cdrs table (columns will be finalized once the CDR generator's output shape is set)

-- ============================
-- departments
-- ============================
CREATE TABLE departments (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE
);

-- ============================
-- employees
-- ============================
CREATE TABLE employees (
    id SERIAL PRIMARY KEY,
    name VARCHAR(150) NOT NULL,
    email VARCHAR(150) NOT NULL UNIQUE,
    department_id INTEGER NOT NULL REFERENCES departments(id),
    job_role VARCHAR(100) NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_employees_department ON employees(department_id);

-- ============================
-- carriers
-- ============================
-- Mobile network operators. Names are unique ignoring case.
CREATE TABLE carriers (
    id SERIAL PRIMARY KEY,
    name VARCHAR(40) NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX idx_carriers_name_lower ON carriers (lower(name));

INSERT INTO carriers (name) VALUES ('Safaricom'), ('Airtel'), ('Telkom');

-- ============================
-- sims
-- ============================
CREATE TABLE sims (
    id SERIAL PRIMARY KEY,
    phone_number VARCHAR(20) NOT NULL UNIQUE,
    iccid VARCHAR(30) NOT NULL UNIQUE,
    carrier_id INTEGER NOT NULL REFERENCES carriers(id) ON DELETE RESTRICT,
    plan_type VARCHAR(50) NOT NULL,
    monthly_cost DECIMAL(10, 2) NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'available'
        CHECK (status IN ('available', 'assigned', 'inactive')),
    -- ML plan tier; picks the ML cohort. Set by PATCH /api/sims/{id}/tier.
    tier VARCHAR(20)
        CHECK (tier IS NULL OR tier IN ('field', 'manager', 'professional', 'support')),
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_sims_status ON sims(status);
CREATE INDEX idx_sims_carrier ON sims(carrier_id);

-- ============================
-- allocations
-- ============================
CREATE TABLE allocations (
    id SERIAL PRIMARY KEY,
    sim_id INTEGER NOT NULL REFERENCES sims(id),
    employee_id INTEGER NOT NULL REFERENCES employees(id),
    assigned_by INTEGER NOT NULL REFERENCES employees(id),
    assignment_date TIMESTAMP NOT NULL DEFAULT NOW(),
    return_date TIMESTAMP
);

CREATE INDEX idx_allocations_sim ON allocations(sim_id);
CREATE INDEX idx_allocations_employee ON allocations(employee_id);

-- ============================
-- alerts
-- ============================
-- resolved_by_user_id points to app_users, the login accounts created by
-- the backend's EF Core migrations (not in this file).
CREATE TABLE alerts (
    id SERIAL PRIMARY KEY,
    sim_id INTEGER NOT NULL REFERENCES sims(id),
    type VARCHAR(50) NOT NULL
        CHECK (type IN ('inactiveBilling', 'sharing', 'reselling', 'ghostUsage', 'other')),
    risk_score DECIMAL(5, 2) NOT NULL
        CHECK (risk_score >= 0 AND risk_score <= 1),
    risk_level VARCHAR(20) NOT NULL
        CHECK (risk_level IN ('high', 'medium', 'low')),
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    status VARCHAR(20) NOT NULL DEFAULT 'flagged'
        CHECK (status IN ('flagged', 'escalated', 'cleared')),
    reasons JSONB NOT NULL DEFAULT '[]'::jsonb,
    model_version VARCHAR(50),
    resolved_at TIMESTAMP,
    resolution VARCHAR(20)
        CHECK (resolution IS NULL OR resolution IN ('confirmedMisuse', 'falsePositive')),
    resolution_note VARCHAR(500),
    resolved_by_user_id INTEGER, -- app_users(id), ON DELETE SET NULL
    -- Scored dates (UTC) the alert covers: first and latest detection.
    -- created_at stays the real time the row was written, also in a backfill.
    detected_on DATE,
    last_detected_on DATE
);

CREATE INDEX idx_alerts_sim ON alerts(sim_id);
CREATE INDEX idx_alerts_status ON alerts(status);
CREATE INDEX idx_alerts_created_at ON alerts(created_at);
CREATE INDEX idx_alerts_model_version ON alerts(model_version);
CREATE INDEX idx_alerts_detected_on ON alerts(detected_on);

-- ============================
-- risk_scores
-- ============================
-- One model score per SIM per day.
CREATE TABLE risk_scores (
    id BIGSERIAL PRIMARY KEY,
    sim_id INTEGER NOT NULL REFERENCES sims(id),
    date DATE NOT NULL,
    probability DECIMAL(5, 4) NOT NULL
        CHECK (probability >= 0 AND probability <= 1),
    bucket VARCHAR(20) NOT NULL
        CHECK (bucket IN ('clear', 'escalated', 'flag')),
    reasons JSONB NOT NULL DEFAULT '[]'::jsonb,
    model_version VARCHAR(50),
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Unique per SIM and day; date descending so it also serves latest-first lookups.
CREATE UNIQUE INDEX idx_risk_scores_sim_date ON risk_scores(sim_id, date DESC);
CREATE INDEX idx_risk_scores_date ON risk_scores(date);
CREATE INDEX idx_risk_scores_model_version ON risk_scores(model_version);

-- ============================
-- daily_usage
-- ============================
-- One row per SIM per day. Columns follow the ML daily usage contract
-- (ml-service/docs/daily_usage_schema.md). Loaded by
-- ml-service/scripts/load_daily_usage.py with upsert on (sim_id, date).
CREATE TABLE daily_usage (
    id BIGSERIAL PRIMARY KEY,
    sim_id INTEGER NOT NULL REFERENCES sims(id),
    date DATE NOT NULL,
    total_download_mb DOUBLE PRECISION NOT NULL,
    total_upload_mb DOUBLE PRECISION NOT NULL,
    data_session_count BIGINT NOT NULL,
    data_duration_s BIGINT NOT NULL,
    hotspot_sessions BIGINT NOT NULL,
    public_apn_sessions BIGINT NOT NULL,
    unique_towers BIGINT NOT NULL,
    geo_flag_count BIGINT NOT NULL,
    peak_mb DOUBLE PRECISION NOT NULL,
    offpeak_mb DOUBLE PRECISION NOT NULL,
    total_mb DOUBLE PRECISION NOT NULL,
    roaming_sessions BIGINT NOT NULL,
    billed_mb DOUBLE PRECISION NOT NULL,
    tier VARCHAR(20) NOT NULL
        CHECK (tier IN ('field', 'manager', 'professional', 'support')),
    voice_minutes_used DOUBLE PRECISION NOT NULL,
    sms_count BIGINT NOT NULL,
    voice_minutes_remaining DOUBLE PRECISION NOT NULL,
    sms_remaining BIGINT NOT NULL
);

CREATE UNIQUE INDEX idx_daily_usage_sim_date ON daily_usage(sim_id, date);
CREATE INDEX idx_daily_usage_date ON daily_usage(date);

-- What the ML Postgres reader selects from: the contract columns only.
-- sims.tier, when set, overrides the tier on each usage row, so a tier
-- change moves the SIM to its new ML cohort for future scores.
CREATE VIEW ml_daily_usage AS
SELECT u.sim_id, u.date,
       u.total_download_mb, u.total_upload_mb,
       u.data_session_count, u.data_duration_s,
       u.hotspot_sessions, u.public_apn_sessions,
       u.unique_towers, u.geo_flag_count,
       u.peak_mb, u.offpeak_mb, u.total_mb,
       u.roaming_sessions, u.billed_mb,
       COALESCE(s.tier, u.tier) AS tier,
       u.voice_minutes_used, u.sms_count,
       u.voice_minutes_remaining, u.sms_remaining
FROM daily_usage u
JOIN sims s ON s.id = u.sim_id;

-- ============================
-- audit_logs
-- ============================
-- user_id is the legacy link to an employee. Rows written by the API set
-- actor_user_id (app_users) and actor_email instead.
CREATE TABLE audit_logs (
    id SERIAL PRIMARY KEY,
    user_id INTEGER REFERENCES employees(id),
    actor_user_id INTEGER, -- app_users(id), ON DELETE SET NULL
    actor_email VARCHAR(256),
    action VARCHAR(100) NOT NULL,
    -- system: user management, auth, roles; organisation: everything else.
    category VARCHAR(20) NOT NULL
        CHECK (category IN ('system', 'organisation')),
    actor_role VARCHAR(20),
    affected_sim_id INTEGER REFERENCES sims(id) ON DELETE SET NULL,
    old_values JSONB,
    new_values JSONB,
    timestamp TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_audit_logs_timestamp ON audit_logs(timestamp);
CREATE INDEX idx_audit_logs_user ON audit_logs(user_id);
CREATE INDEX idx_audit_logs_actor ON audit_logs(actor_user_id);
CREATE INDEX idx_audit_logs_sim ON audit_logs(affected_sim_id);
CREATE INDEX idx_audit_logs_action ON audit_logs(action);
CREATE INDEX idx_audit_logs_category ON audit_logs(category);

-- ============================
-- import_runs
-- ============================
-- One row per CSV import commit (previews are not recorded). See
-- backend/docs/imports.md.
CREATE TABLE import_runs (
    id SERIAL PRIMARY KEY,
    type VARCHAR(20) NOT NULL
        CHECK (type IN ('departments', 'employees', 'sims', 'usage')),
    file_name VARCHAR(255) NOT NULL,
    sha256 VARCHAR(64),
    uploaded_by_user_id INTEGER, -- app_users(id), ON DELETE SET NULL
    uploaded_by_email VARCHAR(256),
    started_at TIMESTAMPTZ NOT NULL,
    finished_at TIMESTAMPTZ NOT NULL,
    status VARCHAR(20) NOT NULL
        CHECK (status IN ('committed', 'rejected', 'failed')),
    skip_invalid BOOLEAN NOT NULL,
    total_rows INTEGER NOT NULL,
    valid_rows INTEGER NOT NULL,
    invalid_rows INTEGER NOT NULL,
    inserted INTEGER NOT NULL,
    updated INTEGER NOT NULL,
    unchanged INTEGER NOT NULL,
    error_count INTEGER NOT NULL,
    warning_count INTEGER NOT NULL,
    message VARCHAR(500)
);

CREATE INDEX idx_import_runs_started_at ON import_runs(started_at);
CREATE INDEX idx_import_runs_type ON import_runs(type);
CREATE INDEX idx_import_runs_uploaded_by ON import_runs(uploaded_by_user_id);

-- ============================
-- scoring_runs
-- ============================
-- One row per scoring run (schedule, manual, import). See
-- backend/docs/scoring-pipeline.md. scope_* is null for a full run.
CREATE TABLE scoring_runs (
    id SERIAL PRIMARY KEY,
    trigger VARCHAR(20) NOT NULL
        CHECK (trigger IN ('schedule', 'manual', 'import')),
    status VARCHAR(20) NOT NULL
        CHECK (status IN ('queued', 'running', 'succeeded', 'partial', 'failed')),
    force BOOLEAN NOT NULL,
    scope_sim_ids INTEGER[],
    scope_from DATE,
    scope_to DATE,
    merged_requests INTEGER NOT NULL,
    requested_by_user_id INTEGER, -- app_users(id), ON DELETE SET NULL
    requested_by VARCHAR(256) NOT NULL, -- user email, or 'system'
    requested_at TIMESTAMPTZ NOT NULL,
    started_at TIMESTAMPTZ,
    finished_at TIMESTAMPTZ,
    first_date DATE,
    last_date DATE,
    dates_scored INTEGER NOT NULL,
    sim_days_scored INTEGER NOT NULL,
    sim_days_skipped INTEGER NOT NULL,
    failures INTEGER NOT NULL,
    alerts_created INTEGER NOT NULL,
    alerts_updated INTEGER NOT NULL,
    error_summary VARCHAR(1000),
    model_version VARCHAR(50)
);

CREATE INDEX idx_scoring_runs_requested_at ON scoring_runs(requested_at);
CREATE INDEX idx_scoring_runs_status ON scoring_runs(status);
CREATE INDEX idx_scoring_runs_requested_by ON scoring_runs(requested_by_user_id);

-- ============================
-- tier recommendations
-- ============================
-- See backend/docs/api-tier-recommendations.md. Nothing is applied
-- automatically; a tier changes only through PATCH /api/sims/{id}/tier.
CREATE TABLE tier_recommendation_settings (
    id INTEGER PRIMARY KEY CHECK (id = 1), -- single row
    interval_days INTEGER NOT NULL CHECK (interval_days BETWEEN 90 AND 365),
    updated_by_user_id INTEGER, -- app_users(id), ON DELETE SET NULL
    updated_by VARCHAR(256),
    updated_at TIMESTAMPTZ
);

INSERT INTO tier_recommendation_settings (id, interval_days) VALUES (1, 90);

CREATE TABLE tier_recommendation_runs (
    id SERIAL PRIMARY KEY,
    trigger VARCHAR(20) NOT NULL
        CHECK (trigger IN ('manual', 'automatic')),
    status VARCHAR(20) NOT NULL
        CHECK (status IN ('running', 'succeeded', 'partial', 'failed')),
    requested_by_user_id INTEGER, -- app_users(id), ON DELETE SET NULL
    requested_by VARCHAR(256) NOT NULL, -- user email, or 'system'
    started_at TIMESTAMPTZ NOT NULL,
    finished_at TIMESTAMPTZ,
    billing_cycle INTEGER,
    sims_evaluated INTEGER NOT NULL,
    recommendations_created INTEGER NOT NULL,
    error_summary VARCHAR(1000)
);

CREATE INDEX idx_tier_rec_runs_started_at ON tier_recommendation_runs(started_at);

CREATE TABLE tier_recommendations (
    id SERIAL PRIMARY KEY,
    sim_id INTEGER NOT NULL REFERENCES sims(id) ON DELETE CASCADE,
    run_id INTEGER NOT NULL REFERENCES tier_recommendation_runs(id) ON DELETE CASCADE,
    current_tier VARCHAR(20) NOT NULL
        CHECK (current_tier IN ('field', 'manager', 'professional', 'support')),
    recommended_tier VARCHAR(20) NOT NULL
        CHECK (recommended_tier IN ('field', 'manager', 'professional', 'support')),
    recommendation VARCHAR(20) NOT NULL
        CHECK (recommendation IN ('upgrade', 'downgrade')),
    reason VARCHAR(500) NOT NULL,
    billing_cycle INTEGER NOT NULL,
    projected_overage_mb DOUBLE PRECISION,
    days_to_depletion INTEGER,
    status VARCHAR(20) NOT NULL
        CHECK (status IN ('open', 'dismissed')),
    created_at TIMESTAMPTZ NOT NULL,
    dismissed_at TIMESTAMPTZ,
    dismissed_by_user_id INTEGER -- app_users(id), ON DELETE SET NULL
);

-- One open recommendation per SIM.
CREATE UNIQUE INDEX idx_tier_recs_one_open_per_sim ON tier_recommendations(sim_id) WHERE status = 'open';
CREATE INDEX idx_tier_recs_sim_status ON tier_recommendations(sim_id, status);
CREATE INDEX idx_tier_recs_status ON tier_recommendations(status);
CREATE INDEX idx_tier_recs_run ON tier_recommendations(run_id);

-- ============================
-- tier allowances
-- ============================
-- What each tier includes per 30-day billing cycle (issue #14). Data bundles
-- match ml-service TIER_BUNDLE_GB (1 GB = 1024 MB); voice and SMS are the
-- cycle totals in the demo usage; costs (KES) are demo values. sims.tier
-- refers to it, so a SIM's monthly cost and allowance come from its tier.
CREATE TABLE tier_allowances (
    tier VARCHAR(20) PRIMARY KEY
        CHECK (tier IN ('field', 'manager', 'professional', 'support')),
    data_mb INTEGER NOT NULL,
    voice_minutes INTEGER NOT NULL,
    sms INTEGER NOT NULL,
    monthly_cost NUMERIC(10,2) NOT NULL,
    CHECK (data_mb > 0 AND voice_minutes >= 0 AND sms >= 0 AND monthly_cost >= 0)
);

INSERT INTO tier_allowances (tier, data_mb, voice_minutes, sms, monthly_cost) VALUES
    ('support', 20480, 100, 70, 1500),
    ('professional', 40960, 200, 135, 2500),
    ('manager', 61440, 300, 205, 3500),
    ('field', 102400, 500, 340, 5000);

ALTER TABLE sims ADD CONSTRAINT fk_sims_tier
    FOREIGN KEY (tier) REFERENCES tier_allowances(tier);
CREATE INDEX idx_sims_tier ON sims(tier);
