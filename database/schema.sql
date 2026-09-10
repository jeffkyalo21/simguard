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
CREATE TABLE alerts (
    id SERIAL PRIMARY KEY,
    sim_id INTEGER NOT NULL REFERENCES sims(id),
    alert_type VARCHAR(50) NOT NULL,
    risk_score DECIMAL(5, 2) NOT NULL,
    risk_level VARCHAR(20) NOT NULL
        CHECK (risk_level IN ('low', 'medium', 'high', 'critical')),
    detected_at TIMESTAMP NOT NULL DEFAULT NOW(),
    status VARCHAR(20) NOT NULL DEFAULT 'open'
        CHECK (status IN ('open', 'investigating', 'resolved'))
);

CREATE INDEX idx_alerts_sim ON alerts(sim_id);
CREATE INDEX idx_alerts_status ON alerts(status);

-- ============================
-- audit_logs
-- ============================
CREATE TABLE audit_logs (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES employees(id),
    action VARCHAR(100) NOT NULL,
    affected_sim_id INTEGER REFERENCES sims(id),
    old_values JSONB,
    new_values JSONB,
    timestamp TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_audit_logs_timestamp ON audit_logs(timestamp);
CREATE INDEX idx_audit_logs_user ON audit_logs(user_id);
