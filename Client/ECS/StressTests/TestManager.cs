#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UltraSim.ECS.StressTests
{
    /// <summary>
    /// Manages stress test execution, sequencing, and results reporting.
    /// </summary>
    public class TestManager
    {
        private World world;
        private StressTestBase? currentTest;
        private Queue<StressTestBase> testQueue = new();
        private List<StressTestResult> completedResults = new();
        private int totalFrames = 0;
        private bool csvExportedAt1000 = false;

        public bool IsRunning => currentTest != null;
        public StressTestBase? CurrentTest => currentTest;
        public IReadOnlyList<StressTestResult> CompletedResults => completedResults;

        public TestManager(World world)
        {
            this.world = world;
        }

        /// <summary>
        /// Gets full path for export file (in project saves directory).
        /// </summary>
        private string GetExportPath(string filename)
        {
            string savesDir = World.Paths.SavesDir;
            string testsDir = Path.Combine(savesDir, "StressTests");

            if (!Directory.Exists(testsDir))
                Directory.CreateDirectory(testsDir);

            return Path.Combine(testsDir, filename);
        }

        /// <summary>
        /// Queues a stress test for execution.
        /// </summary>
        public void QueueTest(StressTestBase test)
        {
            testQueue.Enqueue(test);
            Logging.Logger.Log($"[TestManager] Queued: {test.TestName}");
        }

        /// <summary>
        /// Starts executing queued tests.
        /// </summary>
        public void Start()
        {
            if (testQueue.Count == 0)
            {
                Logging.Logger.Log("[TestManager] No tests queued", Logging.LogSeverity.Warning);
                return;
            }

            totalFrames = 0;
            csvExportedAt1000 = false;
            StartNextTest();
        }

        /// <summary>
        /// Updates the current test. Call this every frame.
        /// </summary>
        public void Update(float deltaTime)
        {
            // Always count frames, even if no test running
            totalFrames++;

            // Auto-export CSV at 1000 frames (if we have any results)
            if (!csvExportedAt1000 && totalFrames >= 1000 && completedResults.Count > 0)
            {
                string path = GetExportPath($"stress_test_1000frames_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                ExportCSV(path);
                csvExportedAt1000 = true;
            }

            if (currentTest == null) return;

            currentTest.Update(deltaTime);

            // Check if test completed or failed
            if (currentTest.IsComplete || currentTest.HasFailed)
            {
                var result = currentTest.GetResults();
                completedResults.Add(result);

                Logging.Logger.Log(result.GenerateReport());

                currentTest.Cleanup();
                currentTest = null;

                // Start next test if any
                if (testQueue.Count > 0)
                {
                    Logging.Logger.Log("\n[TestManager] Starting next test...\n");
                    StartNextTest();
                }
                else
                {
                    Logging.Logger.Log("\n[TestManager] ALL TESTS COMPLETED\n");
                    PrintSummary();

                    // Final export
                    string finalPath = GetExportPath($"stress_test_final_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    ExportCSV(finalPath);
                }
            }
        }

        private void StartNextTest()
        {
            if (testQueue.Count == 0) return;

            currentTest = testQueue.Dequeue();
            currentTest.Initialize();
        }

        /// <summary>
        /// Prints a summary of all completed tests.
        /// </summary>
        public void PrintSummary()
        {
            if (completedResults.Count == 0)
            {
                Logging.Logger.Log("[TestManager] No completed tests to summarize.");
                return;
            }

            Logging.Logger.Log("\n=================================================");
            Logging.Logger.Log("         TEST SUITE SUMMARY                 ");
            Logging.Logger.Log("=================================================");

            int passed = 0;
            int failed = 0;

            foreach (var result in completedResults)
            {
                string status = result.Crashed ? "FAIL" : "PASS";
                if (result.Crashed) failed++; else passed++;

                Logging.Logger.Log($"{result.TestType,-15} {status,-8} {result.AverageFrameTimeMs,8:F2}ms");
            }

            Logging.Logger.Log("=================================================");
            Logging.Logger.Log($"Total Tests: {completedResults.Count,-29}");
            Logging.Logger.Log($"Passed: {passed,-34}");
            Logging.Logger.Log($"Failed: {failed,-34}");
            Logging.Logger.Log("=================================================\n");
        }

        /// <summary>
        /// Exports test results to CSV format for analysis.
        /// </summary>
        public void ExportCSV(string filePath)
        {
            if (completedResults.Count == 0)
            {
                Logging.Logger.Log($"[TestManager] WARNING: No results to export (0 completed tests)");
                return;
            }

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("TestType,Intensity,Status,DurationSeconds,PeakEntities,TotalCreated,TotalDestroyed,AvgFrameMs,PeakFrameMs,MinFrameMs,StartMemoryMB,PeakMemoryMB,EndMemoryMB");

                foreach (var r in completedResults)
                {
                    sb.AppendLine($"{r.TestType},{r.Intensity},{(r.Crashed ? "Failed" : "Passed")},{r.Duration.TotalSeconds:F2}," +
                                  $"{r.PeakEntityCount},{r.TotalEntitiesCreated},{r.TotalEntitiesDestroyed}," +
                                  $"{r.AverageFrameTimeMs:F3},{r.PeakFrameTimeMs:F3},{r.MinFrameTimeMs:F3}," +
                                  $"{r.StartMemoryBytes / 1024.0 / 1024.0:F2},{r.PeakMemoryBytes / 1024.0 / 1024.0:F2},{r.EndMemoryBytes / 1024.0 / 1024.0:F2}");
                }

                File.WriteAllText(filePath, sb.ToString());

                // Print FULL path so user can find it
                string fullPath = Path.GetFullPath(filePath);
                Logging.Logger.Log($"");
                Logging.Logger.Log($"============================================================");
                Logging.Logger.Log($" CSV EXPORTED");
                Logging.Logger.Log($"============================================================");
                Logging.Logger.Log($" Results: {completedResults.Count} tests");
                Logging.Logger.Log($" Frames:  {totalFrames}");
                Logging.Logger.Log($" Path:    {fullPath}");
                Logging.Logger.Log($"============================================================");
                Logging.Logger.Log($"");
            }
            catch (Exception ex)
            {
                Logging.Logger.Log($"", Logging.LogSeverity.Error);
                Logging.Logger.Log($"============================================================", Logging.LogSeverity.Error);
                Logging.Logger.Log($" CSV EXPORT FAILED", Logging.LogSeverity.Error);
                Logging.Logger.Log($"============================================================", Logging.LogSeverity.Error);
                Logging.Logger.Log($" Path:  {filePath}", Logging.LogSeverity.Error);
                Logging.Logger.Log($" Error: {ex.Message}", Logging.LogSeverity.Error);
                Logging.Logger.Log($"============================================================", Logging.LogSeverity.Error);
                Logging.Logger.Log($"", Logging.LogSeverity.Error);
            }
        }

        /// <summary>
        /// Stops the current test and exports CSV if we have results.
        /// </summary>
        public void Stop()
        {
            if (currentTest != null)
            {
                currentTest.Cleanup();
                currentTest = null;
            }

            testQueue.Clear();

            // Export CSV on stop if we have results
            if (completedResults.Count > 0)
            {
                string path = GetExportPath($"stress_test_stopped_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                ExportCSV(path);
            }

            Logging.Logger.Log("[TestManager] Stopped");
        }

        /// <summary>
        /// Clears all completed results.
        /// </summary>
        public void ClearResults()
        {
            completedResults.Clear();
            totalFrames = 0;
            csvExportedAt1000 = false;
            Logging.Logger.Log("[TestManager] Results cleared");
        }
    }
}
