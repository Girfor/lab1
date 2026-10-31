-- A minimal legacy schema for a CLEAN local database and CI only.
-- DO NOT run on an existing production database.
CREATE TABLE IF NOT EXISTS public."Passes" (
    id UUID PRIMARY KEY,
    person_id UUID NOT NULL,
    badge_code VARCHAR(64) NOT NULL,
    valid_until TIMESTAMPTZ NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    is_test BOOLEAN NOT NULL DEFAULT FALSE,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO public."Passes"
  (id, person_id, badge_code, valid_until, is_active, is_test)
VALUES
  ('00000000-0000-4000-8000-000000000101',
   '00000000-0000-4000-8000-000000000201',
   'OLD-CAMPUS-101', '2030-01-01T00:00:00Z', TRUE, FALSE)
ON CONFLICT (id) DO NOTHING;
