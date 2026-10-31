-- CAMPUS-101 | Phase 3: IRREVERSIBLE contract step.
-- Preconditions: new-only mode deployed everywhere, legacy writers drained,
-- backup verified, release/change owner approved the irreversible step.
-- Use: PGOPTIONS='-c campus.contract_approved=yes' psql ... -f sql/03_contract.sql
BEGIN;
SET LOCAL lock_timeout = '3s';

DO $$
BEGIN
    IF current_setting('campus.contract_approved', TRUE) IS DISTINCT FROM 'yes' THEN
        RAISE EXCEPTION 'Contract not approved: set campus.contract_approved=yes';
    END IF;
    -- After the cutover the new table is authoritative: an updated field
    -- may legitimately differ from the (now frozen) old row. Check IDs only.
    IF EXISTS (
        SELECT 1 FROM public."Passes" AS p
        WHERE NOT EXISTS (
            SELECT 1 FROM public."AccessPasses" AS a WHERE a.id = p.id
        )
    ) THEN
        RAISE EXCEPTION 'Unsafe contract: some legacy IDs are missing';
    END IF;
END;
$$;

DROP TRIGGER trg_sync_access_passes ON public."Passes";
DROP FUNCTION public.sync_passes_to_access_passes();
DROP TABLE public."Passes";
COMMIT;
