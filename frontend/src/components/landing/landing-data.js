// Sample data for the Landing page only. The landing page is public, so it
// never calls the API: the tour and the hero preview use these fixed
// values. RESULTS come from the ML test run.

/** The SIM used in "Why it fired" and the SIM profile mock. */
export const SAMPLE_SIM = {
  number: "+254 712 345 678",
  employee: "Employee 0142",
  department: "Ground handling",
  tier: "professional",
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

/** Fixed sample of the dashboard at "Last 30 days" for the hero and tour. */
export const SAMPLE_DASHBOARD = {
  kpis: { activeSims: 1000, flagged: 12, toReview: 18, resolved: 41 },
  dataWaste: {
    paidGb: 14650,
    usedGb: 4540,
    unusedGb: 10110,
    percent: 69,
    departments: [
      { name: "Ground handling", unusedGb: 3420 },
      { name: "Engineering", unusedGb: 2780 },
      { name: "Cabin services", unusedGb: 2150 },
      { name: "Head office", unusedGb: 1760 },
    ],
  },
  needsReview: [
    { id: 1, phoneNumber: "+254712345678", employeeName: "Employee 0142", riskScore: 0.94 },
    { id: 2, phoneNumber: "+254722810364", employeeName: "Employee 0387", riskScore: 0.91 },
    { id: 3, phoneNumber: "+254733402917", employeeName: null, riskScore: 0.67 },
    { id: 4, phoneNumber: "+254701556208", employeeName: "Employee 0815", riskScore: 0.58 },
  ],
};
