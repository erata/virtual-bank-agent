-- DEMO DATA ONLY. Seed after generated customers/accounts and credit applications.
\connect customer_db
-- One new customer and two bank-customer login bindings for isolation testing.
INSERT INTO customer.customers
  (id, branch, first_name, last_name, email, phone, iban, date_of_birth, segment, identity_subject_id)
VALUES ('C-2001', 'MUC', 'Deniz', 'Kaya', 'deniz.kaya@example.test', '+49 711 0000 2001',
        'DE00100100100000002001', '1993-08-19', 'private', '00000000-0000-4000-8000-000000000011') ON CONFLICT (id) DO NOTHING;
INSERT INTO customer.accounts(id, customer_id, type, currency, balance)
VALUES ('A-20011', 'C-2001', 'checking', 'EUR', 4350.25),
       ('A-20012', 'C-2001', 'savings', 'EUR', 9800.00) ON CONFLICT (id) DO NOTHING;
UPDATE customer.customers SET identity_subject_id = '00000000-0000-4000-8000-000000000012'
 WHERE id = 'C-5001';

-- Dates are relative to install so 'next 3 / 5 / 30 days' stays demonstrable.
INSERT INTO customer.scheduled_payments(id,customer_id,description,amount,currency,due_date,status) VALUES
 ('P-2001-01','C-2001','Elektrik faturası',89.90,'EUR',CURRENT_DATE + 3,'planned'),
 ('P-2001-02','C-2001','Kira',890.00,'EUR',CURRENT_DATE + 5,'planned'),
 ('P-2001-03','C-2001','İnternet',29.99,'EUR',CURRENT_DATE + 24,'planned'),
 ('P-5001-01','C-5001','Miete',1200.00,'EUR',CURRENT_DATE + 3,'planned') ON CONFLICT (id) DO NOTHING;
INSERT INTO customer.account_transactions(id,customer_id,description,amount,currency,booked_on) VALUES
 ('T-2001-01','C-2001','Supermarkt',-72.40,'EUR',CURRENT_DATE - 2),
 ('T-2001-02','C-2001','Gehalt',2800.00,'EUR',CURRENT_DATE - 10),
 ('T-2001-03','C-2001','Restaurant',-35.60,'EUR',CURRENT_DATE - 28),
 ('T-5001-01','C-5001','Tankstelle',-75.00,'EUR',CURRENT_DATE - 3) ON CONFLICT (id) DO NOTHING;
INSERT INTO customer.standing_orders(id,customer_id,description,amount,currency,frequency,next_due_date,active) VALUES
 ('SO-2001-01','C-2001','Sparplan',150.00,'EUR','monthly',CURRENT_DATE + 8,true),
 ('SO-2001-02','C-2001','Mitgliedschaft',24.00,'EUR','monthly',CURRENT_DATE + 12,true),
 ('SO-5001-01','C-5001','Miete',1200.00,'EUR','monthly',CURRENT_DATE + 5,true) ON CONFLICT (id) DO NOTHING;
