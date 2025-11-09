#nullable enable

using UltraSim.ECS;
using UltraSim;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Client.ECS.StressTests
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
            Logging.Log($"[TestManager] Queued: {test.TestName}");
        }

        /// <summary>
        /// Starts executing queued tests.
        /// </summary>
        public void Start()
        {
            if (testQueue.Count == 0)
            {
                Logging.Log("[TestManager] No tests queued", LogSeverity.Warning);
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

                Logging.Log(result.GenerateReport());

                currentTest.Cleanup();
                currentTest = null;

                // Start next test if any
                if (testQueue.Count > 0)
                {
                    Logging.Log("\n[TestManager] Starting next test...\n");
                    StartNextTest();
                }
                else
                {
                    Logging.Log("\n[TestManager] ALL TESTS COMPLETED\n");
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
                Logging.Log("[TestManager] No completed tests to summarize.");
                return;
            }

            Logging.Log("\n=================================================");
            Logging.Log("         TEST SUITE SUMMARY                 ");
            Logging.Log("=================================================");

            int passed = 0;
            int failed = 0;

            foreach (var result in completedResults)
            {
                string status = result.Crashed ? "FAIL" : "PASS";
                if (result.Crashed) failed++; else passed++;

                Logging.Log($"{result.TestType,-15} {status,-8} {result.AverageFrameTimeMs,8:F2}ms");
            }

            Logging.Log("=================================================");
            Logging.Log($"Total Tests: {completedResults.Count,-29}");
            Logging.Log($"Passed: {passed,-34}");
            Logging.Log($"Failed: {failed,-34}");
            Logging.Log("=================================================\n");
        }

        /// <summary>
        /// Exports test results to CSV format for analysis.
        /// </summary>
        public void ExportCSV(string filePath)
        {
            if (completedResults.Count == 0)
            {
                Logging.Log($"[TestManager] WARNING: No results to export (0 completed tests)");
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
                Logging.Log($"");
                Logging.Log($"============================================================");
                Logging.Log($" CSV EXPORTED");
                Logging.Log($"============================================================");
                Logging.Log($" Results: {completedResults.Count} tests");
                Logging.Log($" Frames:  {totalFrames}");
                Logging.Log($" Path:    {fullPath}");
                Logging.Log($"============================================================");
                Logging.Log($"");
            }
            catch (Exception ex)
            {
                Logging.Log($"", LogSeverity.Error);
                Logging.Log($"============================================================", LogSeverity.Error);
                Logging.Log($" CSV EXPORT FAILED", LogSeverity.Error);
                Logging.Log($"============================================================", LogSeverity.Error);
                Logging.Log($" Path:  {filePath}", LogSeverity.Error);
                Logging.Log($" Error: {ex.Message}", LogSeverity.Error);
                Logging.Log($"============================================================", LogSeverity.Error);
                Logging.Log($"", LogSeverity.Error);
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

            Logging.Log("[TestManager] Stopped");
        }

        /// <summary>
        /// Clears all completed results.
        /// </summary>
        public void ClearResults()
        {
            completedResults.Clear();
            totalFrames = 0;
            csvExportedAt1000 = false;
            Logging.Log("[TestManager] Results cleared");
        }
    }
}


