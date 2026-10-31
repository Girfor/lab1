-- CAMPUS-101 | Phase 1: add the new table while keeping Passes operational.
-- Must be executed before deploying the dual-write application.
BEGIN;
SET LOCAL lock_timeout = '3s';

CREATE TABLE IF NOT EXISTS public."AccessPasses" (
    id UUID PRIMARY KEY,
    person_id UUID NOT NULL,
    badge_code VARCHAR(64) NOT NULL,
    valid_until TIMESTAMPTZ NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    is_test BOOLEAN NOT NULL DEFAULT FALSE,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Old deployed versions still write only to Passes. The trigger mirrors
-- INSERT, UPDATE and DELETE in the original transaction until cutover.
CREATE OR REPLACE FUNCTION public.sync_passes_to_access_passes()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM public."AccessPasses" WHERE id = OLD.id;
        RETURN OLD;
    END IF;

    INSERT INTO public."AccessPasses"
      (id, person_id, badge_code, valid_until, is_active, is_test, updated_at)
    VALUES
      (NEW.id, NEW.person_id, NEW.badge_code, NEW.valid_until,
       NEW.is_active, NEW.is_test, NEW.updated_at)
    ON CONFLICT (id) DO UPDATE SET
        person_id = EXCLUDED.person_id,
        badge_code = EXCLUDED.badge_code,
        valid_until = EXCLUDED.valid_until,
        is_active = EXCLUDED.is_active,
        is_test = EXCLUDED.is_test,
        updated_at = EXCLUDED.updated_at;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_sync_access_passes ON public."Passes";
CREATE TRIGGER trg_sync_access_passes
AFTER INSERT OR UPDATE OR DELETE ON public."Passes"
FOR EACH ROW EXECUTE FUNCTION public.sync_passes_to_access_passes();

COMMIT;
