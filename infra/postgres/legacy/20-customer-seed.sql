-- GENERATED FILE. Do not edit manually.
-- Source: infra/seed/fixtures.json and infra/keycloak/realm-virtual-bank.json
-- Generator: tools/VirtualBank.SeedGenerator

\connect customer_db

INSERT INTO customer.customers (id, branch, first_name, last_name, email, phone, iban, date_of_birth, segment) VALUES ('C-1002', 'STR', 'Jana', 'Weidner', 'jana.weidner@example.test', '+49 711 0000 1002', 'DE00100100100000001002', '1984-03-12', 'private');
INSERT INTO customer.customers (id, branch, first_name, last_name, email, phone, iban, date_of_birth, segment) VALUES ('C-1003', 'STR', 'Oliver', 'Brandt', 'oliver.brandt@example.test', '+49 711 0000 1003', 'DE00100100100000001003', '1979-11-02', 'private');
INSERT INTO customer.customers (id, branch, first_name, last_name, email, phone, iban, date_of_birth, segment) VALUES ('C-1004', 'STR', 'Selin', 'Aydın', 'selin.aydin@example.test', '+49 711 0000 1004', 'DE00100100100000001004', '1991-06-25', 'business');
INSERT INTO customer.customers (id, branch, first_name, last_name, email, phone, iban, date_of_birth, segment) VALUES ('C-5001', 'MUC', 'Thomas', 'Huber', 'thomas.huber@example.test', '+49 89 0000 5001', 'DE00700700700000005001', '1968-01-30', 'private');
INSERT INTO customer.customers (id, branch, first_name, last_name, email, phone, iban, date_of_birth, segment) VALUES ('C-5002', 'MUC', 'Lisa', 'Gruber', 'lisa.gruber@example.test', '+49 89 0000 5002', 'DE00700700700000005002', '1988-09-14', 'business');
INSERT INTO customer.accounts (id, customer_id, type, currency, balance) VALUES ('A-10021', 'C-1002', 'checking', 'EUR', 4210.55);
INSERT INTO customer.accounts (id, customer_id, type, currency, balance) VALUES ('A-10022', 'C-1002', 'savings', 'EUR', 18300.00);
INSERT INTO customer.accounts (id, customer_id, type, currency, balance) VALUES ('A-10031', 'C-1003', 'checking', 'EUR', 950.10);
INSERT INTO customer.accounts (id, customer_id, type, currency, balance) VALUES ('A-10041', 'C-1004', 'business', 'EUR', 61200.00);
INSERT INTO customer.accounts (id, customer_id, type, currency, balance) VALUES ('A-50011', 'C-5001', 'checking', 'EUR', 2780.40);
INSERT INTO customer.accounts (id, customer_id, type, currency, balance) VALUES ('A-50021', 'C-5002', 'business', 'EUR', 33900.75);

-- Authorization projection derived from creditApplications[].assignedAnalyst.
INSERT INTO customer.credit_assignments (customer_id, analyst_id) VALUES ('C-1002', '00000000-0000-4000-8000-000000000005');
INSERT INTO customer.credit_assignments (customer_id, analyst_id) VALUES ('C-5002', '00000000-0000-4000-8000-000000000005');
