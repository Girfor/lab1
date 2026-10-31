-- CAMPUS-101 | Phase 2: backfill historical rows in small transactions.
-- Run with psql AUTOCOMMIT ON; do not wrap CALL in an explicit BEGIN.
-- Row locks prevent a concurrent DELETE or UPDATE from racing with a batch.
-- SKIP LOCKED avoids waiting for hot rows; the retry loop catches them later.
CREATE OR REPLACE PROCEDURE public.migrate_access_passes(p_batch_size INTEGER)
LANGUAGE plpgsql AS $$
DECLARE
    v_rows INTEGER;
    v_missing BIGINT;
    v_empty_attempts INTEGER := 0;
BEGIN
    IF p_batch_size < 1 OR p_batch_size > 10000 THEN
        RAISE EXCEPTION 'Invalid batch size';
    END IF;

    LOOP
        WITH batch AS (
            SELECT p.id, p.person_id, p.badge_code, p.valid_until,
                   p.is_active, p.is_test, p.updated_at
            FROM public."Passes" AS p
            WHERE NOT EXISTS (
                SELECT 1 FROM public."AccessPasses" AS a WHERE a.id = p.id
            )
            ORDER BY p.id
            LIMIT p_batch_size
            FOR UPDATE OF p SKIP LOCKED
        )
        INSERT INTO public."AccessPasses"
          (id, person_id, badge_code, valid_until, is_active, is_test, updated_at)
        SELECT id, person_id, badge_code, valid_until,
               is_active, is_test, updated_at FROM batch
        ON CONFLICT (id) DO NOTHING;

        GET DIAGNOSTICS v_rows = ROW_COUNT;
        COMMIT; -- release batch row locks before taking the next batch

        IF v_rows = 0 THEN
            SELECT COUNT(*) INTO v_missing
            FROM public."Passes" AS p
            WHERE NOT EXISTS (
                SELECT 1 FROM public."AccessPasses" AS a WHERE a.id = p.id
            );
            EXIT WHEN v_missing = 0;
            v_empty_attempts := v_empty_attempts + 1;
            IF v_empty_attempts > 20 THEN
                RAISE EXCEPTION 'Backfill incomplete: % rows still missing', v_missing;
            END IF;
            PERFORM pg_sleep(0.25);
        ELSE
            v_empty_attempts := 0;
        END IF;
    END LOOP;
END;
$$;

CALL public.migrate_access_passes(250);
DROP PROCEDURE public.migrate_access_passes(INTEGER);

-- Deployment gate: the old dataset must be represented accurately in new.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM public."Passes" AS p
        LEFT JOIN public."AccessPasses" AS a ON a.id = p.id
        WHERE a.id IS NULL OR
              ROW(p.person_id, p.badge_code, p.valid_until,
                  p.is_active, p.is_test) IS DISTINCT FROM
              ROW(a.person_id, a.badge_code, a.valid_until,
                  a.is_active, a.is_test)
    ) THEN
        RAISE EXCEPTION 'CAMPUS-101: source/target rows differ';
    END IF;
END;
$$;
