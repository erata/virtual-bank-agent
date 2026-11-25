-- GENERATED FILE. Do not edit manually.
-- Source: infra/seed/fixtures.json and infra/keycloak/realm-virtual-bank.json
-- Generator: tools/VirtualBank.SeedGenerator

\connect credit_db

INSERT INTO credit.credit_applications (id, customer_id, amount, currency, status, purpose, prepared_by, assigned_analyst_id) VALUES ('K-100', 'C-1002', 35000.00, 'EUR', 'SUBMITTED', 'Renovation loan', '00000000-0000-4000-8000-000000000005', '00000000-0000-4000-8000-000000000005');
INSERT INTO credit.credit_applications (id, customer_id, amount, currency, status, purpose, prepared_by, assigned_analyst_id) VALUES ('K-101', 'C-1002', 80000.00, 'EUR', 'SUBMITTED', 'Property purchase top-up', '00000000-0000-4000-8000-000000000005', '00000000-0000-4000-8000-000000000005');
INSERT INTO credit.credit_applications (id, customer_id, amount, currency, status, purpose, prepared_by, assigned_analyst_id) VALUES ('K-102', 'C-5002', 20000.00, 'EUR', 'SUBMITTED', 'Equipment financing', '00000000-0000-4000-8000-000000000006', '00000000-0000-4000-8000-000000000005');
INSERT INTO credit.credit_applications (id, customer_id, amount, currency, status, purpose, prepared_by, assigned_analyst_id) VALUES ('K-200', 'C-5001', 12000.00, 'EUR', 'DRAFT', 'Car loan', 'unassigned', NULL);
