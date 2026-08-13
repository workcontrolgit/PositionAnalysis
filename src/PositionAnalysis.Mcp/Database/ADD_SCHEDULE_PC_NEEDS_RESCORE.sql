PROMPT Adding Schedule PC needs_rescore column...

DECLARE
    v_column_count PLS_INTEGER;
BEGIN
    BEGIN
        EXECUTE IMMEDIATE 'ALTER TABLE schedule_pc_eval ADD (needs_rescore VARCHAR2(1) DEFAULT ''N'')';
    EXCEPTION
        WHEN OTHERS THEN
            IF SQLCODE != -1430 THEN
                RAISE;
            END IF;
    END;

    SELECT COUNT(*)
    INTO v_column_count
    FROM user_tab_columns
    WHERE table_name = 'SCHEDULE_PC_EVAL'
      AND column_name = 'NEEDS_RESCORE'
      AND data_type = 'VARCHAR2'
      AND char_length = 1;

    IF v_column_count != 1 THEN
        RAISE_APPLICATION_ERROR(
            -20001,
            'NEEDS_RESCORE must be VARCHAR2 with CHAR_LENGTH 1 on SCHEDULE_PC_EVAL');
    END IF;
END;
/

-- Flag every scored PD lacking per-duty-keyed criteria scoring
-- (i.e. a triggered criterion with no SupportingDutyNumbers) for rescore.
PROMPT Flagging PDs missing per-duty-keyed scoring for rescore...

UPDATE schedule_pc_eval
SET needs_rescore = 'Y'
WHERE result_json IS NOT NULL
  AND result_json IS JSON
  AND (
        (JSON_VALUE(result_json, '$.CriteriaScores[0].Triggered') = 'true'
            AND NOT JSON_EXISTS(result_json, '$.CriteriaScores[0].SupportingDutyNumbers[0]'))
     OR (JSON_VALUE(result_json, '$.CriteriaScores[1].Triggered') = 'true'
            AND NOT JSON_EXISTS(result_json, '$.CriteriaScores[1].SupportingDutyNumbers[0]'))
     OR (JSON_VALUE(result_json, '$.CriteriaScores[2].Triggered') = 'true'
            AND NOT JSON_EXISTS(result_json, '$.CriteriaScores[2].SupportingDutyNumbers[0]'))
     OR (JSON_VALUE(result_json, '$.CriteriaScores[3].Triggered') = 'true'
            AND NOT JSON_EXISTS(result_json, '$.CriteriaScores[3].SupportingDutyNumbers[0]'))
      );

COMMIT;

PROMPT Done. Row counts by needs_rescore:
SELECT needs_rescore, COUNT(*) AS cnt
FROM schedule_pc_eval
GROUP BY needs_rescore;
