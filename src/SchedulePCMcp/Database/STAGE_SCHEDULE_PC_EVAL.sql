CREATE OR REPLACE PROCEDURE stage_schedule_pc_eval (
    p_series       IN VARCHAR2 DEFAULT NULL,
    p_org_code     IN VARCHAR2 DEFAULT NULL,
    p_staged_count OUT PLS_INTEGER
) AS
BEGIN
    INSERT INTO schedule_pc_eval (
        pd_seq_num,
        pd_nbr,
        series,
        grade,
        status,
        rating,
        is_candidate,
        justification_summary,
        scored_at,
        result_json,
        error_msg
    )
    SELECT
        pd.pd_seq_num,
        pd.pd_nbr,
        pd.gvt_occ_series,
        LPAD(TRIM(pd.grd_code), 2, '0'),
        'pending',
        'PENDING',
        'N',
        'Staged for evaluation',
        SYSTIMESTAMP,
        NULL,
        NULL
    FROM max_pd_vw pd
    WHERE CASE
              WHEN REGEXP_LIKE(TRIM(pd.grd_code), '^[[:digit:]]+$')
              THEN TO_NUMBER(TRIM(pd.grd_code))
          END BETWEEN 13 AND 15
      AND (p_series IS NULL OR pd.gvt_occ_series = p_series)
      AND (p_org_code IS NULL OR pd.pd_origin_org_code = p_org_code);

    p_staged_count := SQL%ROWCOUNT;
END;
/