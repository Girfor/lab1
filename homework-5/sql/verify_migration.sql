-- Verification of historic copy and legacy write/delete synchronization.
-- This test leaves zero new records: its changes are rolled back.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM public."AccessPasses"
        WHERE id = '00000000-0000-4000-8000-000000000101'
    ) THEN RAISE EXCEPTION 'Historical row was not migrated'; END IF;
END;
$$;

BEGIN;
INSERT INTO public."Passes"
  (id, person_id, badge_code, valid_until, is_active, is_test)
VALUES
  ('00000000-0000-4000-8000-000000000301',
   '00000000-0000-4000-8000-000000000201',
   'SANITY-LEGACY', '2030-01-01T00:00:00Z', TRUE, TRUE);

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM public."AccessPasses"
      WHERE id = '00000000-0000-4000-8000-000000000301') THEN
    RAISE EXCEPTION 'INSERT trigger failed';
  END IF;
END $$;

UPDATE public."Passes" SET is_active = FALSE
WHERE id = '00000000-0000-4000-8000-000000000301';

DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM public."AccessPasses"
      WHERE id = '00000000-0000-4000-8000-000000000301'
      AND is_active = TRUE) THEN RAISE EXCEPTION 'UPDATE trigger failed'; END IF;
END $$;

DELETE FROM public."Passes"
WHERE id = '00000000-0000-4000-8000-000000000301';

DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM public."AccessPasses"
      WHERE id = '00000000-0000-4000-8000-000000000301') THEN
    RAISE EXCEPTION 'DELETE trigger failed';
  END IF;
END $$;
ROLLBACK;
