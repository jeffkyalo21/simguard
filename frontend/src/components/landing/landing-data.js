// Mock data for the Landing page only. The tour's profile and alerts
// screens use these sample values; its dashboard screen and the hero
// preview read getSampleDashboard() instead. RESULTS come from the ML
// test run.

/** The SIM used in "Why it fired" and the SIM profile mock. */
export const SAMPLE_SIM = {
  number: "+254 712 345 678",
  employee: "Employee 0142",
  department: "Ground handling",
  tier: "Professional",
  operator: "Carrier A",
  iccid: "8925 4010 0000 0142",
  status: "assigned",
  risk: 0.94,
  usedGb: 27.5,
  limitGb: 30,
};

/**
 * ablation_summary.csv, variant "full" (ml-service MLflow run f6c535d6),
 * synthetic test data. Shown rounded down: auto flag precision 0.9858,
 * recall 0.8964 (sharing) to 0.9416 (ghost usage).
 */
export const RESULTS = {
  precision: 98,
  recallLow: 89,
  recallHigh: 94,
  types: 4,
};

export const TOUR_ALERTS = [
  { id: 1, number: "+254 712 345 678", type: "ghostUsage", score: 0.94, date: "28 Oct" },
  { id: 2, number: "+254 722 118 904", type: "sharing", score: 0.88, date: "27 Oct" },
  { id: 3, number: "+254 733 560 221", type: "reselling", score: 0.71, date: "26 Oct" },
  { id: 4, number: "+254 701 442 019", type: "inactiveBilling", score: 0.63, date: "24 Oct" },
  { id: 5, number: "+254 745 908 330", type: "sharing", score: 0.32, date: "22 Oct" },
];
