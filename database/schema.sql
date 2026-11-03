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
    resolved_by_user_id INTEGER -- app_users(id), ON DELETE SET NULL
);

CREATE INDEX idx_alerts_sim ON alerts(sim_id);
CREATE INDEX idx_alerts_status ON alerts(status);
CREATE INDEX idx_alerts_created_at ON alerts(created_at);
CREATE INDEX idx_alerts_model_version ON alerts(model_version);

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
CREATE VIEW ml_daily_usage AS
SELECT sim_id, date,
       total_download_mb, total_upload_mb,
       data_session_count, data_duration_s,
       hotspot_sessions, public_apn_sessions,
       unique_towers, geo_flag_count,
       peak_mb, offpeak_mb, total_mb,
       roaming_sessions, billed_mb, tier,
       voice_minutes_used, sms_count,
       voice_minutes_remaining, sms_remaining
FROM daily_usage;

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
