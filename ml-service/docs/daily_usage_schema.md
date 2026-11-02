# Daily usage schema (`daily_df`)

This is the contract for the input to `build_features(daily_df)` in
`src/feature_engineering.py`, and for the future database view that will feed it.
It matches `data/synthetic_cdr_daily_agg.csv`, the raw file the training notebooks
start from.

## Row rules

- One row per SIM per calendar day. `(sim_id, date)` is unique.
- Sorted by `sim_id`, then `date` ascending. `build_features` sorts again, but the
  view should return rows in this order.
- Days have no gaps. A day with no activity is a row with zeros, not a missing row.
  Rolling windows count rows, not calendar days, so a gap shifts every window.
- Send at least 14 days before the first day you want scored, per SIM. The 7 and 14
  day rolling features and the 14 day trend slope only see the rows passed in. With
  less history the values differ from training (the first row of a SIM gets the
  fill values in `artifacts/feature_fill_values.json`).
- No column is nullable. The training data has no nulls, and the saved X files are
  checked for zero NaN and zero inf.
- Units: data in megabytes (MB), durations in seconds, voice in minutes.

## Columns

| Column | Type | Meaning | Nullable |
| --- | --- | --- | --- |
| `sim_id` | int64 | SIM identifier. | No |
| `date` | date (or ISO `YYYY-MM-DD` string) | Calendar day the row covers. | No |
| `total_download_mb` | float64 | Data downloaded that day, MB. | No |
| `total_upload_mb` | float64 | Data uploaded that day, MB. | No |
| `data_session_count` | int64 | Number of data sessions that day. | No |
| `data_duration_s` | int64 | Total data session time, seconds. | No |
| `hotspot_sessions` | int64 | Sessions that used tethering or hotspot. | No |
| `public_apn_sessions` | int64 | Sessions on the public APN instead of the corporate one. | No |
| `unique_towers` | int64 | Distinct cell towers seen that day. | No |
| `geo_flag_count` | int64 | Sessions flagged for an unexpected location. | No |
| `peak_mb` | float64 | Data used in peak hours, MB. | No |
| `offpeak_mb` | float64 | Data used in off-peak hours, MB. | No |
| `total_mb` | float64 | All data used that day, MB. Download plus upload. | No |
| `roaming_sessions` | int64 | Sessions while roaming. | No |
| `billed_mb` | float64 | Data the carrier billed for that day, MB. | No |
| `tier` | string | Plan tier. One of `field`, `manager`, `professional`, `support`. Any other value is rejected. | No |
| `voice_minutes_used` | float64 | Voice minutes used that day. | No |
| `sms_count` | int64 | SMS sent that day. | No |
| `voice_minutes_remaining` | float64 | Voice minutes left in the bundle at end of day. Negative values are clipped to 0 for `voice_utilization_ratio`. | No |
| `sms_remaining` | int64 | SMS left in the bundle at end of day. | No |

Extra columns are ignored. Label columns (`is_anomalous`, `misuse_category`) must
not be sent to the scoring service.
