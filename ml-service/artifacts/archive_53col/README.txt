archive_53col: artifacts of the old 53 column XGBoost triage model (xgboost_triage.ipynb).
Kept for reference only. The live model is in artifacts/ (42 columns, xgboost_final.ipynb).

Known issue with the SHAP files in this folder
(shap_values_escalated.npy, shap_base_value.json, shap_summary.png):

They were computed on all 104 trees in the booster instead of the best 84
(best_iteration 83). Early stopping keeps the extra trees in the booster,
but predict_proba only uses the first 84, so these SHAP values explain a
different model from the one that made the predictions.

Saved base value: 0.10657 log-odds. Base value of the 84 tree model: 0.07462.

The files are left unchanged on purpose. Do not use them to explain this
model's predictions. The model file and its probabilities are not affected.
