-- Count of PDs flagged needs_rescore = 'Y', grouped by series.
SELECT series, COUNT(*) AS remaining_flagged
FROM schedule_pc_eval
WHERE needs_rescore = 'Y'
GROUP BY series
ORDER BY remaining_flagged DESC;
