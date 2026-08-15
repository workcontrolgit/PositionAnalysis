PROMPT Creating SCHEDULE_PC_EVAL table...

CREATE TABLE schedule_pc_eval (
    pd_seq_num              NUMBER          NOT NULL,
    pd_nbr                  VARCHAR2(20)    NOT NULL,
    series                  VARCHAR2(10),
    grade                   VARCHAR2(5),
    status                  VARCHAR2(20),
    rating                  VARCHAR2(20),
    is_candidate            VARCHAR2(20),
    justification_summary   CLOB,
    scored_at               TIMESTAMP,
    result_json             CLOB,
    error_msg               VARCHAR2(1000),
    needs_rescore           VARCHAR2(1) DEFAULT 'N',
    worker_id               VARCHAR2(128),
    claimed_at              TIMESTAMP,
    lease_expires_at        TIMESTAMP,
    CONSTRAINT pk_schedule_pc_eval PRIMARY KEY (pd_seq_num)
);

CREATE INDEX ix_schedule_pc_eval_queue ON schedule_pc_eval (status, lease_expires_at, pd_seq_num);

PROMPT SCHEDULE_PC_EVAL created.
