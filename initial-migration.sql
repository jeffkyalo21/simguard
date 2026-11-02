CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE departments (
    id serial NOT NULL,
    name character varying(100) NOT NULL,
    CONSTRAINT "PK_departments" PRIMARY KEY (id)
);

CREATE TABLE sims (
    id serial NOT NULL,
    phone_number character varying(20) NOT NULL,
    iccid character varying(30) NOT NULL,
    operator character varying(50) NOT NULL,
    plan_type character varying(50) NOT NULL,
    monthly_cost numeric(10,2) NOT NULL,
    status character varying(20) NOT NULL DEFAULT 'available',
    created_at timestamp without time zone NOT NULL DEFAULT (NOW()),
    CONSTRAINT "PK_sims" PRIMARY KEY (id),
    CONSTRAINT "CK_sims_status" CHECK (status IN ('available', 'assigned', 'inactive'))
);

CREATE TABLE employees (
    id serial NOT NULL,
    name character varying(150) NOT NULL,
    email character varying(150) NOT NULL,
    department_id integer NOT NULL,
    job_role character varying(100) NOT NULL,
    created_at timestamp without time zone NOT NULL DEFAULT (NOW()),
    CONSTRAINT "PK_employees" PRIMARY KEY (id),
    CONSTRAINT "FK_employees_departments_department_id" FOREIGN KEY (department_id) REFERENCES departments (id)
);

CREATE TABLE alerts (
    id serial NOT NULL,
    sim_id integer NOT NULL,
    alert_type character varying(50) NOT NULL,
    risk_score numeric(5,2) NOT NULL,
    risk_level character varying(20) NOT NULL,
    detected_at timestamp without time zone NOT NULL DEFAULT (NOW()),
    status character varying(20) NOT NULL DEFAULT 'open',
    CONSTRAINT "PK_alerts" PRIMARY KEY (id),
    CONSTRAINT "CK_alerts_risk_level" CHECK (risk_level IN ('low', 'medium', 'high', 'critical')),
    CONSTRAINT "CK_alerts_status" CHECK (status IN ('open', 'investigating', 'resolved')),
    CONSTRAINT "FK_alerts_sims_sim_id" FOREIGN KEY (sim_id) REFERENCES sims (id)
);

CREATE TABLE allocations (
    id serial NOT NULL,
    sim_id integer NOT NULL,
    employee_id integer NOT NULL,
    assigned_by integer NOT NULL,
    assignment_date timestamp without time zone NOT NULL DEFAULT (NOW()),
    return_date timestamp without time zone,
    CONSTRAINT "PK_allocations" PRIMARY KEY (id),
    CONSTRAINT "FK_allocations_employees_assigned_by" FOREIGN KEY (assigned_by) REFERENCES employees (id),
    CONSTRAINT "FK_allocations_employees_employee_id" FOREIGN KEY (employee_id) REFERENCES employees (id),
    CONSTRAINT "FK_allocations_sims_sim_id" FOREIGN KEY (sim_id) REFERENCES sims (id)
);

CREATE TABLE audit_logs (
    id serial NOT NULL,
    user_id integer NOT NULL,
    action character varying(100) NOT NULL,
    affected_sim_id integer,
    old_values jsonb,
    new_values jsonb,
    timestamp timestamp without time zone NOT NULL DEFAULT (NOW()),
    CONSTRAINT "PK_audit_logs" PRIMARY KEY (id),
    CONSTRAINT "FK_audit_logs_employees_user_id" FOREIGN KEY (user_id) REFERENCES employees (id),
    CONSTRAINT "FK_audit_logs_sims_affected_sim_id" FOREIGN KEY (affected_sim_id) REFERENCES sims (id)
);

CREATE INDEX idx_alerts_sim ON alerts (sim_id);

CREATE INDEX idx_alerts_status ON alerts (status);

CREATE INDEX idx_allocations_employee ON allocations (employee_id);

CREATE INDEX idx_allocations_sim ON allocations (sim_id);

CREATE INDEX "IX_allocations_assigned_by" ON allocations (assigned_by);

CREATE INDEX idx_audit_logs_timestamp ON audit_logs (timestamp);

CREATE INDEX idx_audit_logs_user ON audit_logs (user_id);

CREATE INDEX "IX_audit_logs_affected_sim_id" ON audit_logs (affected_sim_id);

CREATE UNIQUE INDEX "IX_departments_name" ON departments (name);

CREATE INDEX idx_employees_department ON employees (department_id);

CREATE UNIQUE INDEX "IX_employees_email" ON employees (email);

CREATE INDEX idx_sims_status ON sims (status);

CREATE UNIQUE INDEX "IX_sims_iccid" ON sims (iccid);

CREATE UNIQUE INDEX "IX_sims_phone_number" ON sims (phone_number);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261021214952_InitialCreate', '10.0.13');

COMMIT;

