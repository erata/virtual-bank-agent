\connect customer_db
SET ROLE customer_owner;

CREATE TABLE IF NOT EXISTS customer.scheduled_payments (
    id text PRIMARY KEY,
    customer_id text NOT NULL REFERENCES customer.customers(id),
    description text NOT NULL,
    amount numeric(14,2) NOT NULL,
    currency char(3) NOT NULL,
    due_date date NOT NULL,
    status text NOT NULL CHECK (status IN ('planned', 'completed', 'cancelled'))
);
CREATE INDEX IF NOT EXISTS scheduled_payments_customer_due ON customer.scheduled_payments(customer_id, due_date);

CREATE TABLE IF NOT EXISTS customer.account_transactions (
    id text PRIMARY KEY,
    customer_id text NOT NULL REFERENCES customer.customers(id),
    description text NOT NULL,
    amount numeric(14,2) NOT NULL,
    currency char(3) NOT NULL,
    booked_on date NOT NULL
);
CREATE INDEX IF NOT EXISTS account_transactions_customer_date ON customer.account_transactions(customer_id, booked_on);

CREATE TABLE IF NOT EXISTS customer.standing_orders (
    id text PRIMARY KEY,
    customer_id text NOT NULL REFERENCES customer.customers(id),
    description text NOT NULL,
    amount numeric(14,2) NOT NULL,
    currency char(3) NOT NULL,
    frequency text NOT NULL,
    next_due_date date NOT NULL,
    active boolean NOT NULL DEFAULT true
);
CREATE INDEX IF NOT EXISTS standing_orders_customer_due ON customer.standing_orders(customer_id, next_due_date);

ALTER TABLE customer.scheduled_payments ENABLE ROW LEVEL SECURITY;
ALTER TABLE customer.scheduled_payments FORCE ROW LEVEL SECURITY;
ALTER TABLE customer.account_transactions ENABLE ROW LEVEL SECURITY;
ALTER TABLE customer.account_transactions FORCE ROW LEVEL SECURITY;
ALTER TABLE customer.standing_orders ENABLE ROW LEVEL SECURITY;
ALTER TABLE customer.standing_orders FORCE ROW LEVEL SECURITY;

-- Never rely on a customer ID supplied by the browser/model; require bank-customer permission
-- and the owner mapping on the authoritative Customer row.
DROP POLICY IF EXISTS scheduled_self_read ON customer.scheduled_payments;
CREATE POLICY scheduled_self_read ON customer.scheduled_payments FOR SELECT TO customer_api
    USING (authz.has_permission('customer.read.self') AND EXISTS
      (SELECT 1 FROM customer.customers c WHERE c.id = scheduled_payments.customer_id
       AND c.identity_subject_id = authz.principal_id()));
DROP POLICY IF EXISTS transaction_self_read ON customer.account_transactions;
CREATE POLICY transaction_self_read ON customer.account_transactions FOR SELECT TO customer_api
    USING (authz.has_permission('customer.read.self') AND EXISTS
      (SELECT 1 FROM customer.customers c WHERE c.id = account_transactions.customer_id
       AND c.identity_subject_id = authz.principal_id()));
DROP POLICY IF EXISTS standing_self_read ON customer.standing_orders;
CREATE POLICY standing_self_read ON customer.standing_orders FOR SELECT TO customer_api
    USING (authz.has_permission('customer.read.self') AND EXISTS
      (SELECT 1 FROM customer.customers c WHERE c.id = standing_orders.customer_id
       AND c.identity_subject_id = authz.principal_id()));
RESET ROLE;
GRANT SELECT ON customer.scheduled_payments, customer.account_transactions, customer.standing_orders TO customer_api;
