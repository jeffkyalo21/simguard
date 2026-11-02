# Report tables

Numbers behind the figures in `docs/figures/` and `docs/evaluation_report.md`. Source: `notebooks/end_to_end_evaluation.ipynb`, MLflow run `11d45da2a7d14f708f550d9bb116300f` (experiment simguard-stage5-evaluation). Per-row figures were rescored with the saved models (no refit) and checked against that run.

### Figure 1: flag vs not flag, test (counts)

|  | not flag | flag |
| --- | --- | --- |
| actual normal | 13,134 | 5 |
| actual anomalous | 62 | 299 |

### Figure 1: flag vs not flag, validation (counts)

|  | not flag | flag |
| --- | --- | --- |
| actual normal | 12,974 | 2 |
| actual anomalous | 74 | 450 |

### Figure 2: gate buckets, test (counts)

|  | clear | escalated | flag |
| --- | --- | --- | --- |
| actual normal | 9,296 | 3,838 | 5 |
| actual anomalous | 13 | 49 | 299 |

### Figure 2: gate buckets, validation (counts)

|  | clear | escalated | flag |
| --- | --- | --- | --- |
| actual normal | 9,123 | 3,851 | 2 |
| actual anomalous | 32 | 42 | 450 |

### Figure 3: actual category vs flag, test (categories are not predicted, only flagged)

|  | not flag | flag | flag share |
| --- | --- | --- | --- |
| normal | 13,134 | 5 | 0.0004 |
| ghost_usage | 8 | 72 | 0.9000 |
| unauthorized_sharing | 43 | 192 | 0.8170 |
| data_reselling | 8 | 16 | 0.6667 |
| inactive_billing | 3 | 19 | 0.8636 |

### Figure 4: gate operating points, test (AP 0.9297, ROC AUC 0.9720)

|  | precision | recall | false positive rate |
| --- | --- | --- | --- |
| p >= 0.10 (not clear) | 0.0830 | 0.9640 | 0.2925 |
| p > 0.90 (flag) | 0.9836 | 0.8283 | 0.0004 |

### Figure 5: stage comparison, test

| stage | AP | recall at 5% FPR |
| --- | --- | --- |
| Isolation Forest alone | 0.4944 | 0.7175 |
| cohort Z alone (cohort_z_iso_anomaly_score) | 0.6143 | 0.7396 |
| XGBoost 42 columns (final) | 0.9297 | 0.9335 |
| XGBoost 53 columns (old design, reference) | 0.9357 | 0.9446 |

### Figure 6: top 10 features by mean |SHAP|, test

|  | mean abs SHAP (log-odds) |
| --- | --- |
| peak_mb | 0.9134 |
| cohort_z_iso_anomaly_score | 0.3954 |
| hotspot_session_ratio | 0.2963 |
| offpeak_ratio | 0.2031 |
| offpeak_mb | 0.0979 |
| hotspot_sum_7d | 0.0959 |
| bytes_per_session | 0.0942 |
| hotspot_sessions | 0.0791 |
| cohort_z_vol_zscore_14d | 0.0740 |
| cohort_z_total_mb | 0.0490 |

### Figure 7: clear threshold trade-off (flag fixed at 0.90)

| clear threshold | val cleared share | val band rows | val band share | val anomalies cleared | test cleared share | test band rows | test band share | test anomalies cleared |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0.05 | 0.0052 | 12,978 | 0.9613 | 0 | 0.0058 | 13,118 | 0.9717 | 0 |
| 0.075 | 0.2055 | 10,274 | 0.7610 | 7 | 0.1935 | 10,584 | 0.7840 | 3 |
| 0.1 | 0.6781 | 3,893 | 0.2884 | 32 | 0.6896 | 3,887 | 0.2879 | 13 |
| 0.125 | 0.8718 | 1,279 | 0.0947 | 36 | 0.8830 | 1,276 | 0.0945 | 21 |
| 0.15 | 0.9223 | 597 | 0.0442 | 38 | 0.9367 | 550 | 0.0407 | 24 |
| 0.2 | 0.9479 | 252 | 0.0187 | 40 | 0.9606 | 228 | 0.0169 | 27 |
| 0.25 | 0.9541 | 168 | 0.0124 | 45 | 0.9671 | 140 | 0.0104 | 27 |

### Figure 8: stability per seed (test part of each split)

| seed | best_iteration | test anomalous SIMs | AP | flag precision | flag recall | band share | anomalies in clear |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 57 | 28 | 0.9177 | 0.9727 | 0.8515 | 0.2765 | 23 |
| 2 | 70 | 27 | 0.9328 | 0.9875 | 0.8780 | 0.1407 | 27 |
| 3 | 68 | 26 | 0.9464 | 0.9927 | 0.9042 | 0.1967 | 15 |
| 4 | 14 | 15 | 0.9063 | 0.0000 | 0.0000 | 1.0000 | 0 |
| 5 | 42 | 27 | 0.9145 | 0.9892 | 0.8415 | 0.7368 | 6 |

### Figure 8: stability mean and standard deviation

|  | AP | flag precision | flag recall | band share | anomalies in clear |
| --- | --- | --- | --- | --- | --- |
| mean | 0.9236 | 0.7884 | 0.6950 | 0.4701 | 14.2000 |
| std | 0.0160 | 0.4408 | 0.3893 | 0.3784 | 11.3004 |

### Figure 9: SIM-level results (precision is one category against normal SIMs)

| split, group | SIMs | flagged | recall | precision | per 100 normal SIMs |
| --- | --- | --- | --- | --- | --- |
| val, all anomalous | 37 | 32 | 0.8649 | 1.0000 |  |
| val, ghost_usage | 10 | 10 | 1.0000 | 1.0000 |  |
| val, unauthorized_sharing | 18 | 14 | 0.7778 | 1.0000 |  |
| val, data_reselling | 7 | 6 | 0.8571 | 1.0000 |  |
| val, inactive_billing | 5 | 5 | 1.0000 | 1.0000 |  |
| val, normal (false alarms) | 113 | 0 |  |  | 0.0000 |
| test, all anomalous | 23 | 23 | 1.0000 | 0.9200 |  |
| test, ghost_usage | 8 | 8 | 1.0000 | 0.8000 |  |
| test, unauthorized_sharing | 10 | 10 | 1.0000 | 0.8333 |  |
| test, data_reselling | 4 | 4 | 1.0000 | 0.6667 |  |
| test, inactive_billing | 3 | 3 | 1.0000 | 0.6000 |  |
| test, normal (false alarms) | 127 | 2 |  |  | 1.5748 |

### Per-category recall, SIM-days

| split, category | anomalous rows | recall flag | recall flag+escalated | in clear |
| --- | --- | --- | --- | --- |
| val, ghost_usage | 150 | 0.9333 | 0.9400 | 9 |
| val, unauthorized_sharing | 217 | 0.8203 | 0.9493 | 11 |
| val, data_reselling | 101 | 0.8020 | 0.9307 | 7 |
| val, inactive_billing | 56 | 0.9107 | 0.9107 | 5 |
| test, ghost_usage | 80 | 0.9000 | 0.9625 | 3 |
| test, unauthorized_sharing | 235 | 0.8170 | 0.9787 | 5 |
| test, data_reselling | 24 | 0.6667 | 0.8750 | 3 |
| test, inactive_billing | 22 | 0.8636 | 0.9091 | 2 |

### Gate metrics, SIM-days

| split, positive | precision | recall | f1 | AP | ROC AUC |
| --- | --- | --- | --- | --- | --- |
| val, flag vs rest | 0.9956 | 0.8588 | 0.9221 | 0.9254 | 0.9620 |
| val, flag + escalated vs clear | 0.1132 | 0.9389 | 0.2021 | 0.9254 | 0.9620 |
| test, flag vs rest | 0.9836 | 0.8283 | 0.8992 | 0.9297 | 0.9720 |
| test, flag + escalated vs clear | 0.0830 | 0.9640 | 0.1529 | 0.9297 | 0.9720 |

### Buckets, SIM-days

| split, bucket | rows | share | anomalies | share of anomalies | precision |
| --- | --- | --- | --- | --- | --- |
| val, clear | 9,155 | 0.6781 | 32 | 0.0611 | 0.0035 |
| val, escalated | 3,893 | 0.2884 | 42 | 0.0802 | 0.0108 |
| val, flag | 452 | 0.0335 | 450 | 0.8588 | 0.9956 |
| test, clear | 9,309 | 0.6896 | 13 | 0.0360 | 0.0014 |
| test, escalated | 3,887 | 0.2879 | 49 | 0.1357 | 0.0126 |
| test, flag | 304 | 0.0225 | 299 | 0.8283 | 0.9836 |
