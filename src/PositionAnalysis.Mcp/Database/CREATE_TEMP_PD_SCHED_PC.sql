PROMPT Creating TEMP_PD_SCHED_PC and TEMP_PD_SCHED_PC_DUTIES tables...

CREATE TABLE temp_pd_sched_pc (
    pd_seq_num                 NUMBER,
    pd_nbr                     VARCHAR2(6),
    pd_effective_date          VARCHAR2(100),
    pd_origin_org_code         VARCHAR2(9),
    org_desc                   VARCHAR2(20),
    bureau_code                VARCHAR2(20),
    bureau_desc                VARCHAR2(20),
    gvt_pay_plan               VARCHAR2(2),
    grd_code                   VARCHAR2(3),
    gvt_occ_series              VARCHAR2(5),
    pd_position_title_text     VARCHAR2(100),
    pd_manager_level           VARCHAR2(2),
    position_sensitivity       VARCHAR2(2),
    position_sensitivity_desc  VARCHAR2(100),
    security_clearance         VARCHAR2(20),
    security_clearance_desc    VARCHAR2(100),
    gvt_comp_level              VARCHAR2(4),
    gm_public_trust             VARCHAR2(3),
    position_occupied_code     VARCHAR2(1),
    position_occupied_desc     VARCHAR2(100),
    pd_intro                   CLOB,
    schedule_pc_ind             VARCHAR2(1),
    CONSTRAINT pk_temp_pd_sched_pc PRIMARY KEY (pd_seq_num)
);

CREATE INDEX ix_temp_pd_sched_pc_pd_nbr ON temp_pd_sched_pc (pd_nbr);
CREATE INDEX ix_temp_pd_sched_pc_series ON temp_pd_sched_pc (gvt_occ_series);

CREATE TABLE temp_pd_sched_pc_duties (
    pdd_seq_num             NUMBER,
    pd_seq_num              NUMBER,
    pdd_percent_time_spent  NUMBER,
    pdd_major_duties_text   CLOB,
    CONSTRAINT pk_temp_pd_sched_pc_duties PRIMARY KEY (pdd_seq_num),
    CONSTRAINT fk_duties_pd_seq_num FOREIGN KEY (pd_seq_num) REFERENCES temp_pd_sched_pc (pd_seq_num)
);

PROMPT TEMP_PD_SCHED_PC and TEMP_PD_SCHED_PC_DUTIES created.
