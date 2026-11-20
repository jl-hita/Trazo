interface IntelligenceSummary {
  from: string; to: string; generatedAt: string;
  plans: { total: number; generated: number; manual: number; engineVersions: string[] };
  prescriptions: { sessions: number; withOriginalSnapshot: number; withoutOriginalSnapshot: number };
  workouts: { total: number; completed: number; partial: number; notCompleted: number };
  adaptations: { kind: string; source: string | null; count: number }[];
  observations: {
    muscle: string; quality: string; count: number; prescribedSets: number; performedSets: number;
    performanceSamples: number; fatigueSignals: number; averagePerformanceChange: number | null;
  }[];
  decisions: { action: string; policyVersion: string; count: number }[];
}
