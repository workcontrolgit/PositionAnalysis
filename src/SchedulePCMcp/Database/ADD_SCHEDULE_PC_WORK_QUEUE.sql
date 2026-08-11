PROMPT Adding Schedule PC worker queue columns...

DECLARE
    v_column_count PLS_INTEGER;
BEGIN
    BEGIN
        EXECUTE IMMEDIATE 'ALTER TABLE schedule_pc_eval ADD (worker_id VARCHAR2(128))';
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
      AND column_name = 'WORKER_ID'
      AND data_type = 'VARCHAR2'
      AND char_length = 128;

    IF v_column_count != 1 THEN
        RAISE_APPLICATION_ERROR(
            -20001,
            'WORKER_ID must be VARCHAR2 with CHAR_LENGTH 128 on SCHEDULE_PC_EVAL');
    END IF;
END;
/

DECLARE
    v_column_count PLS_INTEGER;
BEGIN
    BEGIN
        EXECUTE IMMEDIATE 'ALTER TABLE schedule_pc_eval ADD (claimed_at TIMESTAMP)';
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
      AND column_name = 'CLAIMED_AT'
      AND data_type = 'TIMESTAMP';

    IF v_column_count != 1 THEN
        RAISE_APPLICATION_ERROR(
            -20001,
            'CLAIMED_AT must be TIMESTAMP on SCHEDULE_PC_EVAL');
    END IF;
END;
/

DECLARE
    v_column_count PLS_INTEGER;
BEGIN
    BEGIN
        EXECUTE IMMEDIATE 'ALTER TABLE schedule_pc_eval ADD (lease_expires_at TIMESTAMP)';
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
      AND column_name = 'LEASE_EXPIRES_AT'
      AND data_type = 'TIMESTAMP';

    IF v_column_count != 1 THEN
        RAISE_APPLICATION_ERROR(
            -20001,
            'LEASE_EXPIRES_AT must be TIMESTAMP on SCHEDULE_PC_EVAL');
    END IF;
END;
/

PROMPT Creating Schedule PC worker queue index...

DECLARE
        v_object_count PLS_INTEGER;

        PROCEDURE validate_queue_index IS
                v_expected_index_count  PLS_INTEGER;
                v_column_count          PLS_INTEGER;
                v_expected_column_count PLS_INTEGER;
        BEGIN
                SELECT COUNT(*)
                INTO v_expected_index_count
                FROM user_indexes
                WHERE index_name = 'IX_SCHEDULE_PC_EVAL_QUEUE'
                    AND table_name = 'SCHEDULE_PC_EVAL'
                    AND status = 'VALID'
                    AND index_type = 'NORMAL'
                    AND uniqueness = 'NONUNIQUE';

                SELECT COUNT(*)
                INTO v_column_count
                FROM user_ind_columns
                WHERE index_name = 'IX_SCHEDULE_PC_EVAL_QUEUE'
                    AND table_name = 'SCHEDULE_PC_EVAL';

                SELECT COUNT(*)
                INTO v_expected_column_count
                FROM user_ind_columns
                WHERE index_name = 'IX_SCHEDULE_PC_EVAL_QUEUE'
                    AND table_name = 'SCHEDULE_PC_EVAL'
                    AND ((column_position = 1 AND column_name = 'STATUS' AND descend = 'ASC')
                        OR (column_position = 2 AND column_name = 'LEASE_EXPIRES_AT' AND descend = 'ASC')
                        OR (column_position = 3 AND column_name = 'PD_SEQ_NUM' AND descend = 'ASC'));

                IF v_expected_index_count != 1
                     OR v_column_count != 3
                     OR v_expected_column_count != 3 THEN
                        RAISE_APPLICATION_ERROR(
                                -20001,
                                'IX_SCHEDULE_PC_EVAL_QUEUE must be a valid nonunique normal index on SCHEDULE_PC_EVAL (STATUS ASC, LEASE_EXPIRES_AT ASC, PD_SEQ_NUM ASC)');
                END IF;
        END validate_queue_index;
BEGIN
    SELECT COUNT(*)
    INTO v_object_count
    FROM user_objects
    WHERE object_name = 'IX_SCHEDULE_PC_EVAL_QUEUE';

    IF v_object_count = 0 THEN
        BEGIN
            EXECUTE IMMEDIATE '
                CREATE INDEX ix_schedule_pc_eval_queue
                    ON schedule_pc_eval (status, lease_expires_at, pd_seq_num)';
        EXCEPTION
            WHEN OTHERS THEN
                IF SQLCODE = -955 THEN
                    validate_queue_index;
                ELSE
                    RAISE;
                END IF;
        END;
    ELSE
        validate_queue_index;
    END IF;

    validate_queue_index;
END;
/

PROMPT Schedule PC worker queue migration complete.