# Stage 4: case based reasoning (CBR) design

Status: evaluated and not adopted (issue #9 step 5c). The design below is kept as written. Band numbers come from `notebooks/xgboost_final.ipynb`.

## Result

- Design A: FAIL on both validation and test (MLflow run 5151094e136b4f4ca14590d52fa7a69e).
- Share of escalated rows resolved: 2.2% on validation and 1.5% on test, far below the 30% target.
- 0 band anomalies wrongly cleared, and no rows flagged, so flags were switched off.
- The simple rival (raising the clear threshold) clears 3.3% on validation and 11.3% on test, more than CBR on both.
- Design B was not built. The anomalies in the band look like normal rows in feature space, and the test split has already been used once.
- `similarCases` stays an empty list in the API.
- The CBR code and case base were removed after the evaluation. The result is kept here and in the executed `notebooks/cbr_evaluation.ipynb`.

## 1. The problem

- The XGBoost gate sends SIM-days with probability 0.10 to 0.90 to "escalated". That is 28.8% of all rows (3,893 of 13,500 on validation, 3,887 on test).
- The band is almost all normal: precision 0.0108 on validation (42 anomalous rows) and 0.0126 on test (49 rows).
- The 42 validation anomalies come from only 19 SIMs (28 unauthorized_sharing, 13 data_reselling, 1 ghost_usage), so the real sample is about 19 SIMs, not 42 rows.
- CBR is not meant to lift a good precision higher. Its job is to settle as many escalated rows as it safely can, mostly by clearing normal ones, without clearing the few real anomalies.
- Every row CBR cannot settle stays escalated for an analyst, with the matched cases attached as evidence.

## 2. Candidate designs

Both designs return the same evidence for every SIM-day: the 5 most similar of the retrieved cases (from both classes), each with sim_id, date, label (normal or anomalous), misuse_category and similarity (0 to 1). That fills `similarCases` in `/v1/score`.

Both designs use the same decision rule. Each query retrieves the 5 nearest normal cases and the 5 nearest anomalous cases separately (k/2 = 5 each). From these:
- a1 = distance to the nearest anomalous case
- A5 = mean distance to the 5 nearest anomalous cases
- N5 = mean distance to the 5 nearest normal cases

The rule, checked in this order:
- **clear** when a1 > d_clear AND N5 * r <= a1. No anomalous case is close, and the normal cases are at least r times closer than the nearest anomalous one.
- **flag** when A5 < d_flag AND A5 * r <= N5. The anomalous cases are close, and at least r times closer than the normal ones.
- **keep escalated** in every other case.

Three tuned parameters: d_clear, d_flag and the ratio r. They are tuned on training data only, by grid search on the out-of-fold band rows (section 3). d_clear and d_flag are searched over distance quantiles, and r over 1.5, 2 and 3. The chosen setting resolves the most rows while wrongly clearing at most 1 anomaly in each fold. Validation and test are never used for tuning.

| | (A) Training SIM-days, feature distance weighted by mean absolute SHAP | (B) Similarity of SHAP vectors |
| --- | --- | --- |
| Case contains | The 42 scaled features, label, misuse_category, sim_id, date | The 42 SHAP values (log-odds), the model probability, label, misuse_category, sim_id, date |
| Similarity | Weighted Euclidean distance on the scaled features. Each feature's weight is its mean absolute SHAP on training data. Similarity = 1 / (1 + distance) | Cosine similarity of the SHAP vectors, plus a small penalty for the gap in probability |
| k | 10 (5 normal plus 5 anomalous) | 10 (5 normal plus 5 anomalous) |
| Pros | Simple. The cases do not depend on the model version, only the weights do. Can catch signals the model underweights | Compares "why the model is unsure", so similar doubts are matched. Directly explainable to analysts |
| Cons | Distances get blurry in 42 dimensions. Features the model ignores still count a little | SHAP on training rows is in-sample, so it looks too confident unless computed out-of-fold. The whole case base must be rebuilt after every retrain |

## 3. Case base rules

- **Training split only** (700 SIMs, 63,000 rows, 1,292 anomalous rows from 102 SIMs). Validation and test SIMs never enter the case base.
- **Out-of-fold scores**: refit XGBoost in 5 folds grouped by SIM on the training split. Each training row then gets a probability and SHAP from a model that never saw that row's SIM. This avoids in-sample confidence, and it tells us which training rows would have landed in the band.
- **Size**: keep all 1,292 anomalous rows. Sample about 6,000 normal rows, with two thirds from the out-of-fold band (the hard cases) and one third random.
- **Avoid all-normal neighbours**: search the normal and anomalous cases separately (k/2 from each class). The decision rule compares distances, not raw counts, so the 99% normal share cannot drown the anomalous cases. Cap each list at 3 days per SIM so one SIM cannot fill it.
- **Leakage**: the scaler, the SHAP weights and the thresholds all come from training data only. The query SIM's own days are never matched, and labels are never part of the query.
- **Refresh**: rebuild after every model retrain (required for B, needed for A's weights). Later, add analyst-confirmed cases with a `label_source` field (synthetic or analyst). Version the case base file and show its sha256 in `/version`.

## 4. Evaluation

Run only on escalated rows of validation and test. Tune on training data first, check on validation, then run once on test at the end.

| Metric | Doing nothing |
| --- | --- |
| Share of escalated rows resolved (cleared plus flagged) | 0% |
| Band anomalies wrongly cleared (recall inside the band) | 0 of 42 (val), 0 of 49 (test) |
| Precision of rows CBR flags | not applicable |
| Size and precision of what stays escalated | 3,893 at 0.0108 (val), 3,887 at 0.0126 (test) |

- **Uncertainty**: with about 19 anomalous SIMs per split, use a bootstrap that resamples SIMs (1,000 times) to get 95% intervals. Also tune with 5 times repeated 5-fold CV grouped by SIM on train plus validation SIMs, to check that the thresholds are stable.
- **Simple rival**: raising the clear threshold above 0.10 also shrinks the band. Compare CBR with that threshold at the same number of missed anomalies. CBR must resolve clearly more rows than it does.
- **Worth keeping if, on both validation and test**:
  - at most 1 band anomaly is wrongly cleared;
  - the lower end of the 95% interval for rows resolved is at least 30%;
  - CBR beats the simple threshold rival;
  - flagged rows have precision of at least 0.5. If not, CBR may still clear rows but never flags.
- **If it is not worth keeping**: CBR makes no decisions. It can still return the matched cases as evidence for analysts, or `similarCases` stays an empty list. The band stays fully manual, and the result goes in the README known limitations.

## 5. Recommendation

- Use design A as the default. It is cheaper and its cases survive retrains.
- Build B only if A falls short on validation (see Build order), and keep it only if it clearly beats A. B adds the out-of-fold SHAP step and a rebuild after every retrain.

## Build order

1. Out-of-fold scores and the case base.
2. Design A and the decision rule.
3. The evaluation code, including the bootstrap and the simple threshold rival.
4. Run A on validation. Decide on B only after seeing that result.
5. API wiring of `similarCases` for whichever design is kept, then one final run on test.

| Work | Hours |
| --- | --- |
| Out-of-fold scores and case base | 4 |
| Design A plus decision rule | 4 |
| Evaluation code, bootstrap and threshold rival | 6 |
| Design B on the same code | 4 |
| API wiring of `similarCases`, tests, docs | 4 |
| Total | 22 (18 if B is skipped) |

Check: 4 + 4 + 6 + 4 + 4 = 22, and 22 minus 4 for B = 18.

## Decisions

1. CBR may clear rows on its own. Every row CBR clears is logged for audit, with its matched cases and the parameter values used.
2. The accepted miss rate is up to 1 anomaly in the band per split. This is an assumption, and the evaluation report states it.
3. Future work, to be listed in the README known limitations:
   - anonymising other customers' data shown as evidence;
   - real labels after launch, and analyst-confirmed cases;
   - using a SIM's own past labelled days as cases.
