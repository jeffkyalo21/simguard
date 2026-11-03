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
-- sims
-- ============================
CREATE TABLE sims (
    id SERIAL PRIMARY KEY,
    phone_number VARCHAR(20) NOT NULL UNIQUE,
    iccid VARCHAR(30) NOT NULL UNIQUE,
    operator VARCHAR(50) NOT NULL,
    plan_type VARCHAR(50) NOT NULL,
    monthly_cost DECIMAL(10, 2) NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'available'
        CHECK (status IN ('available', 'assigned', 'inactive')),
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_sims_status ON sims(status);

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
