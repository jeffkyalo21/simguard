# End-to-end evaluation of the scoring pipeline

Issue #10. Notebook: `notebooks/end_to_end_evaluation.ipynb`. MLflow experiment `simguard-stage5-evaluation`, run `11d45da2a7d14f708f550d9bb116300f` (one nested run per stability seed).

## What was tested

Every score below comes from the raw daily table (`data/synthetic_cdr_daily_agg.csv`), passed through `src.risk_scoring.score`, which is the code the API runs. The labels were rebuilt from the episode file and match the saved label files. The SIM split is the same one used in `xgboost_final.ipynb`: 700 training, 150 validation and 150 test SIMs, each with 90 days. The model was chosen on validation. Test was only looked at, never used to pick anything. The gate is unchanged: clear below 0.10, flag above 0.90, escalated in between.

| Split | Anomalous SIMs | Anomalous rows | ghost_usage | unauthorized_sharing | data_reselling | inactive_billing |
| --- | --- | --- | --- | --- | --- | --- |
| Validation | 37 | 524 | 150 rows, 10 SIMs | 217 rows, 18 SIMs | 101 rows, 7 SIMs | 56 rows, 5 SIMs |
| Test | 23 | 361 | 80 rows, 8 SIMs | 235 rows, 10 SIMs | 24 rows, 4 SIMs | 22 rows, 3 SIMs |

**Pipeline check.** On validation and test, the probabilities from `score()` are identical to the notebook probabilities (the largest difference is 0.0), so every row lands in the same bucket.

## Results per SIM-day

| Split | AP | ROC AUC | Flag: precision / recall / F1 | Flag or escalated vs clear: precision / recall |
| --- | --- | --- | --- | --- |
| Validation | 0.925 | 0.962 | 0.996 / 0.859 / 0.922 | 0.113 / 0.939 |
| Test | 0.930 | 0.972 | 0.984 / 0.828 / 0.899 | 0.083 / 0.964 |

| Bucket | Validation rows (anomalies) | Test rows (anomalies) |
| --- | --- | --- |
| Clear | 9,155 (32) | 9,309 (13) |
| Escalated | 3,893, 28.8% (42) | 3,887, 28.8% (49) |
| Flag | 452 (450) | 304 (299) |

Share of each category's anomalous rows that got flagged (validation, then test): ghost_usage 0.93 and 0.90, unauthorized_sharing 0.82 and 0.82, data_reselling 0.80 and 0.67, inactive_billing 0.91 and 0.86. Counting flagged and escalated rows together, at least 0.87 of every category is caught. data_reselling on test is based on only 24 rows from 4 SIMs.

## What each stage adds

All four scores were computed on the same rows. "Recall at 5% FPR" means: set the threshold so that 5% of normal rows are flagged, then count how many anomalous rows are caught.

| Score | Validation AP | Validation recall at 5% FPR | Test AP | Test recall at 5% FPR |
| --- | --- | --- | --- | --- |
| Isolation Forest alone | 0.463 | 0.584 | 0.494 | 0.718 |
| Best cohort Z alone (cohort_z_iso_anomaly_score) | 0.536 | 0.630 | 0.614 | 0.740 |
| XGBoost, 42 columns (final) | 0.925 | 0.928 | 0.930 | 0.934 |
| XGBoost, 53 columns (old design, reference only) | 0.929 | 0.931 | 0.936 | 0.945 |

Comparing the unsupervised scores within each tier gives a small lift. Supervised XGBoost gives the big lift. The old 53-column model scores slightly higher, but its Prophet inputs looked ahead within the billing cycle (`prophet_lookahead_audit.ipynb`). Its small edge cannot be trusted, and it is shown only as a reference.

## Results per SIM

A SIM counts as flagged if at least one of its days is flagged. For each category, precision compares that category's SIMs against the normal SIMs only.

| Split | Anomalous SIMs flagged | Precision | False alarms per 100 normal SIMs | Days from episode start to first flag: median / 90th percentile |
| --- | --- | --- | --- | --- |
| Validation | 32 of 37 (0.86) | 1.00 | 0 (0 of 113) | 1 / 2 (34 of 40 episodes detected) |
| Test | 23 of 23 (1.00) | 0.92 | 1.6 (2 of 127) | 1 / 2.6 (25 of 25 detected) |

By category on validation: every ghost_usage and inactive_billing SIM was flagged. 14 of 18 unauthorized_sharing SIMs and 6 of 7 data_reselling SIMs were flagged. On test, every SIM in every category was flagged. Because the same two false-alarm SIMs count against every category, the per-category test precision ranges from 0.60 (inactive_billing, 3 SIMs) to 0.83. Most episodes are flagged on their first or second day.

## Clear threshold trade-off (flag stays at 0.90)

| Clear threshold | Validation: share cleared / band rows / anomalies cleared | Test: share cleared / band rows / anomalies cleared |
| --- | --- | --- |
| 0.05 | 0.5% / 12,978 / 0 | 0.6% / 13,118 / 0 |
| 0.075 | 20.6% / 10,274 / 7 | 19.4% / 10,584 / 3 |
| 0.10 (current) | 67.8% / 3,893 / 32 | 69.0% / 3,887 / 13 |
| 0.125 | 87.2% / 1,279 / 36 | 88.3% / 1,276 / 21 |
| 0.15 | 92.2% / 597 / 38 | 93.7% / 550 / 24 |
| 0.20 | 94.8% / 252 / 40 | 96.1% / 228 / 27 |
| 0.25 | 95.4% / 168 / 45 | 96.7% / 140 / 27 |

Nearly half of all rows (47% on validation) have probabilities between 0.075 and 0.10, so the current clear line sits right at the edge of a large cluster of low scores. On validation, moving the line to 0.125 cuts the band from 3,893 rows to 1,279, at the cost of clearing 4 more anomalous rows. Test shows the same pattern. Nothing was changed: any new threshold has to be chosen on validation, as a separate decision.

## Stability over other splits

Five new SIM splits (seeds 1 to 5, 700/150/150 SIMs each) were made. For each, the scaler, Isolation Forest, cohort stats and XGBoost were refitted on that split's training SIMs, with early stopping on its validation SIMs. Results are on each split's test SIMs. All five took 18 seconds in total, and nothing was saved to disk.

| Seed | Trees kept | Test anomalous SIMs | AP | Flag precision | Flag recall | Band share |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 58 | 28 | 0.918 | 0.973 | 0.852 | 27.7% |
| 2 | 71 | 27 | 0.933 | 0.988 | 0.878 | 14.1% |
| 3 | 69 | 26 | 0.946 | 0.993 | 0.904 | 19.7% |
| 4 | 15 | 15 | 0.906 | none flagged | 0.000 | 100% |
| 5 | 43 | 27 | 0.915 | 0.989 | 0.842 | 73.7% |
| Mean (std) | | | 0.924 (0.016) | 0.788 (0.441) | 0.695 (0.389) | 47.0% (37.8%) |

**The ranking is stable, but the gate is not.** AP only varies by about 0.02. The fixed 0.10 and 0.90 thresholds, however, depend on how many trees early stopping keeps, because the probabilities are not calibrated. With scale_pos_weight around 48, every score starts out high, and only spreads below 0.10 or above 0.90 once enough trees have been added. For seed 4 only 15 trees were kept, so every test probability fell between 0.23 and 0.77, and the whole test set landed in the band. Even a refit on the original split (seed 42) changes the result slightly, from 3,887 band rows to 4,330. This is because the pipeline features differ from the saved training files by tiny rounding amounts (about 0.0000003), which is enough to change XGBoost's histogram bins. The scaler, Isolation Forest and cohort statistics were reproduced exactly. In practice: after any retrain, the gate thresholds must be re-derived on validation before deploying the model, following the "Retraining checklist" in `ml-service/README.md`.

## Speed

Scoring 1,000 SIMs for one day takes 2.1 seconds when read through the service's parquet reader and `score()` (best of 3). Through the API it takes about 1.8 seconds in-process and 2.0 to 2.1 seconds over HTTP, as measured in issue #9.

## Validation checks

Notebook: `notebooks/validation_checks.ipynb`. MLflow run `669595fe07084312b755ecb8a89366bf` (earlier attempts are tagged `invalid=true`). All refits stayed in memory.

**Split.** No SIM id appears in more than one split, every SIM keeps all 90 days in its own split, and the split code reproduces the saved split. There is no department field, so tiers are shown.

| Split | SIMs | Rows | Anomalous SIMs | Anomalous rows | ghost / sharing / reselling / inactive rows | field / manager / professional / support SIMs |
| --- | --- | --- | --- | --- | --- | --- |
| Train | 700 | 63,000 | 102 | 1,292 | 447 / 403 / 293 / 149 | 46 / 226 / 318 / 110 |
| Validation | 150 | 13,500 | 37 | 524 | 150 / 217 / 101 / 56 | 8 / 54 / 71 / 17 |
| Test | 150 | 13,500 | 23 | 361 | 80 / 235 / 24 / 22 | 9 / 49 / 69 / 23 |

**Label shuffle.** The training labels were shuffled 5 times, and XGBoost was refitted with the final settings and a fixed 57 trees. Validation AP came out between 0.021 and 0.032, and test AP between 0.014 and 0.018, against base rates of 0.039 and 0.027. This passes the rule fixed before the run (at most 2 times the base rate). The early-stopping version (shown in the notebook, not asserted) reached 0.18 once, because early stopping picks the tree count on the real validation labels.

**No future days.** For 30 test SIMs and 3 dates each, all days after the scored date were removed and the SIM was scored again. All 90 probabilities were identical to the full-history run (largest difference 0.0).

**Columns.** None of the 42 columns is read from the label file. Two names contain "anomaly": `has_geo_anomaly` (a velocity flag from the session data) and `cohort_z_iso_anomaly_score` (the unsupervised Isolation Forest score). Some columns mirror how the generator builds a category, and single columns can separate one category from normal rows almost perfectly:
- `billing_ratio` and `billing_gap_mb` against inactive_billing (AUC 0.95; billed exceeds used on 91% of those rows and on no normal row).
- `total_mb` and `total_upload_mb` against ghost_usage (0.97).
- The hotspot columns against unauthorized_sharing (0.92).
- `offpeak_ratio` against data_reselling (0.94).

These are real signals an operator would also see, so they are kept. On real data they will be much less clean.

**Fit on train only.** The scaler, Isolation Forest, cohort statistics and XGBoost were each refitted on the 700 training SIMs. Each reproduced the saved artifact exactly (largest difference 7e-15). Fitting on all 1,000 SIMs changes every one of them, so the check can tell the two apart. XGBoost used validation only for early stopping.

**Error analysis (test, flag at p > 0.90).**

| Category | Anomalous rows | Missed, escalated | Missed, cleared | Missed rows on episode day 0 |
| --- | --- | --- | --- | --- |
| ghost_usage | 80 | 5 | 3 | 8 |
| unauthorized_sharing | 235 | 38 | 5 | 10 |
| data_reselling | 24 | 5 | 3 | 4 |
| inactive_billing | 22 | 1 | 2 | 3 |

1. **The first labelled day of every episode carries no anomaly.** In the generator, the day a SIM enters an episode is labelled, but its usage is still drawn as normal. The category signature is present on 0 to 4% of start days and on 96 to 100% of the next days. These rows make up 25 of the 62 missed rows, and all 13 anomalies that the gate cleared. Without them, test flag recall is 0.89 instead of 0.83.
2. **The other misses are weak forms of the pattern.**
   - Missed sharing rows have a median of 1 hotspot session and 1.5 times normal volume (caught rows: 3 sessions and 2.5 times). Most of them land in the escalated band.
   - data_reselling adds a burst on only about 30% of days. Every burst day was flagged, and every missed reselling row was a day without a burst.
3. **False alarms are normal days that look like misuse by chance.** The 5 false positive rows, 2 of them on SIMs that never had an episode, follow two patterns:
   - No peak-hour traffic at all (3 rows, like reselling).
   - 2 or 3 hotspot sessions on one day (2 rows, like sharing).

   Each of the 2 false-alarm SIMs was flagged on one day only.

**Alternative models.** Same 42 columns and training rows. Every choice was made on validation, including each model's flag threshold (the lowest threshold that reaches 0.95 validation precision).

| Model | Test AP | Test ROC AUC | Test recall at 5% FPR | Threshold | Test flag precision / recall |
| --- | --- | --- | --- | --- | --- |
| Isolation Forest alone | 0.494 | 0.910 | 0.718 | 0.090 | 0.696 / 0.260 |
| Logistic regression (C 0.01) | 0.814 | 0.960 | 0.917 | 0.989 | 0.952 / 0.496 |
| Random forest (300 trees, leaf 5) | 0.933 | 0.972 | 0.934 | 0.408 | 0.953 / 0.903 |
| HistGradientBoosting (490 iterations) | 0.928 | 0.968 | 0.928 | 0.266 | 0.921 / 0.906 |
| XGBoost (final) | 0.930 | 0.972 | 0.934 | 0.774 | 0.961 / 0.881 |
| XGBoost at the production gate | | | | 0.90 | 0.984 / 0.828 |

XGBoost is well ahead of logistic regression and Isolation Forest: XGBoost was ahead of both in every one of 1,000 SIM bootstrap resamples. It is level with random forest and HistGradientBoosting: the 95% intervals of the AP gaps include 0. On accuracy, XGBoost is justified over the simple models but not over the other tree ensembles. It is kept for practical reasons: it is already deployed, it is small (57 trees), and its built-in SHAP gives the API's reasons. Random forest would be an equally valid choice. The README rule applied to the current model would put the flag threshold at 0.774 rather than 0.90 (test precision 0.961, recall 0.881). Nothing was changed.

## List of figures

The numbers behind each figure are in [report_tables.md](report_tables.md).

1. [Flag vs not flag, test](figures/confusion_matrix_flag_test.png) and [validation](figures/confusion_matrix_flag_val.png)
2. [Gate buckets (clear, escalated, flag), test](figures/confusion_matrix_gate_test.png) and [validation](figures/confusion_matrix_gate_val.png)
3. [Actual category vs flag, test](figures/confusion_matrix_category_test.png)
4. [Precision-recall curve, test](figures/pr_curve_test.png) and [ROC curve, test](figures/roc_curve_test.png), with the 0.10 and 0.90 gate points
5. [What each stage adds, test](figures/stage_comparison_test.png)
6. [Top 10 features by mean absolute SHAP, test](figures/shap_top10_test.png)
7. [Clear threshold trade-off](figures/clear_threshold_tradeoff.png)
8. [Stability over 5 refitted splits](figures/stability_splits.png)
9. [SIM-level recall and false alarms](figures/sim_level_summary.png)

## Limitations

- **Synthetic data.** Every anomaly comes from a generator with four clean categories and clear start and end days. Real misuse will be messier, and real labels will be late and incomplete.
- **Small samples.** Each split has only 23 to 37 anomalous SIMs, and some categories have just 3 to 5. One SIM can move a recall figure by 10 to 30 points, so the per-category figures are rough.
- **The escalated band.** 28.8% of rows go to the band, almost all of them normal (precision about 0.01). The stability check shows the band's size depends heavily on the trained model.
- **No calibration yet.** The probabilities are not calibrated, so the gate thresholds only hold for the model they were tuned on. Calibrating them (for example isotonic or Platt scaling on validation) is future work. Until then, follow the README "Retraining checklist".
- **CBR not adopted.** Case based reasoning settled only 2.2% (validation) and 1.5% (test) of band rows (`docs/cbr_design.md`). Analysts review the band using the SHAP reasons.
- **Billing cycle assumption.** Cycles are fixed 30-day blocks from 2025-01-01 (`billing_cycle_num = days // 30`). Real cycles follow each customer's billing date and differ in length.
- **First-day fill value.** It was computed on all 90,000 rows before the split (README known limitations). Its effect is tiny, and the stability refits reuse it.
- **Prophet (tier recommendation), evaluated separately and not rerun here.** The forecast error for the end of the cycle shrinks as the cycle goes on: about 2,436 MB at day 10, 716 MB at day 20 and 347 MB at day 25. Fitting 3,000 SIM-cycles at 3 checkpoints took 35.8 minutes with no errors. The old forecast, fitted on the whole cycle, looked ahead: the gap between forecast and actual was larger on late-cycle rows (median ratio 1.317 vs 1.017 on control rows, p = 1.1e-05), and early-row AP went from 0.023 to 0.059. That is why the forecast inputs were removed from the triage model. The old fit gave 2,378 downgrade, 610 no change and 12 upgrade recommendations out of 3,000 cycles. SIMs with little history (ghost_usage, inactive_billing) give weak forecasts.
