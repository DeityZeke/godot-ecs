#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace UltraSim.ECS.Testing
{
    /// <summary>
    /// Manages stress test execution, sequencing, and results reporting.
    /// FIXED: CSV export on Stop, ESC, and better conditions
    /// </summary>
    public class StressTestManager
    {
        private World world;
        private StressTestModule? currentTest;
        private Queue<StressTestModule> testQueue = new();
        private List<StressTestResult> completedResults = new();
        private int totalFrames = 0;
        private bool csvExportedAt1000 = false;
        
        public bool IsRunning => currentTest != null;
        public StressTestModule? CurrentTest => currentTest;
        public IReadOnlyList<StressTestResult> CompletedResults => completedResults;

        public StressTestManager(World world)
        {
            this.world = world;
        }

        /// <summary>
        /// Gets full path for export file (in exe directory).
        /// </summary>
        private string GetExportPath(string filename)
        {
            // Use OS.GetExecutablePath() to get exe directory
            string exePath = Godot.OS.GetExecutablePath();
            string exeDir = System.IO.Path.GetDirectoryName(exePath) ?? "./";
            string fullPath = System.IO.Path.Combine(exeDir, filename);
            return fullPath;
        }

        /// <summary>
        /// Queues a stress test for execution.
        /// </summary>
        public void QueueTest(StressTestModule test)
        {
            testQueue.Enqueue(test);
#if USE_DEBUG
            GD.Print($"[StressTestManager] Queued: {test.TestName}");
#endif // USE_DEBUG
        }

        /// <summary>
        /// Queues all standard stress tests with the given intensity.
        /// </summary>
        public void QueueAllTests(StressIntensity intensity)
        {
            QueueTest(new SpawnStressTest(world, StressTestConfig.CreatePreset(StressTestType.Spawn, intensity)));
            QueueTest(new ChurnStressTest(world, StressTestConfig.CreatePreset(StressTestType.Churn, intensity)));
            QueueTest(new ArchetypeStressTest(world, StressTestConfig.CreatePreset(StressTestType.Archetype, intensity)));
            
#if USE_DEBUG
            GD.Print($"[StressTestManager] Queued all tests at {intensity} intensity");
#endif // USE_DEBUG
        }

        /// <summary>
        /// Starts executing queued tests.
        /// </summary>
        public void Start()
        {
            if (testQueue.Count == 0)
            {
#if USE_DEBUG
                GD.PrintErr("[StressTestManager] No tests queued!");
#endif // USE_DEBUG
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
                
#if USE_DEBUG
                GD.Print(result.GenerateReport());
#endif // USE_DEBUG
                
                currentTest.Cleanup();
                currentTest = null;

                // Start next test if any
                if (testQueue.Count > 0)
                {
#if USE_DEBUG
                    GD.Print("\n[StressTestManager] Starting next test...\n");
#endif // USE_DEBUG
                    StartNextTest();
                }
                else
                {
#if USE_DEBUG
                    GD.Print("\n[StressTestManager] ✅ ALL TESTS COMPLETED\n");
                    PrintSummary();
#endif // USE_DEBUG
                    
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
#if USE_DEBUG
                GD.Print("[StressTestManager] No completed tests to summarize.");
#endif // USE_DEBUG
                return;
            }

#if USE_DEBUG
            GD.Print("\n╔═══════════════════════════════════════════════╗");
            GD.Print("║         TEST SUITE SUMMARY                 ║");
            GD.Print("╠═══════════════════════════════════════════════╣");

            int passed = 0;
            int failed = 0;

            foreach (var result in completedResults)
            {
                string status = result.Crashed ? "❌ FAIL" : "✅ PASS";
                if (result.Crashed) failed++; else passed++;

                GD.Print($"║ {result.TestType,-15} {status,-8} {result.AverageFrameTimeMs,8:F2}ms ║");
            }

            GD.Print("╠═══════════════════════════════════════════════╣");
            GD.Print($"║ Total Tests: {completedResults.Count,-29} ║");
            GD.Print($"║ Passed: {passed,-34} ║");
            GD.Print($"║ Failed: {failed,-34} ║");
            GD.Print("╚═══════════════════════════════════════════════╝\n");
#endif // USE_DEBUG
        }

        /// <summary>
        /// Exports all test results to a text file.
        /// </summary>
        public void ExportResults(string filePath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("═══════════════════════════════════════════════════");
                sb.AppendLine("           STRESS TEST RESULTS EXPORT");
                sb.AppendLine($"           Generated: {DateTime.Now}");
                sb.AppendLine("═══════════════════════════════════════════════════\n");

                foreach (var result in completedResults)
                {
                    sb.AppendLine(result.GenerateReport());
                    sb.AppendLine();
                }

                sb.AppendLine("\n═══════════════════════════════════════════════════");
                sb.AppendLine("                    SUMMARY");
                sb.AppendLine("═══════════════════════════════════════════════════");
                
                int passed = 0, failed = 0;
                foreach (var r in completedResults)
                {
                    if (r.Crashed) failed++; else passed++;
                }

                sb.AppendLine($"Total Tests: {completedResults.Count}");
                sb.AppendLine($"Passed: {passed}");
                sb.AppendLine($"Failed: {failed}");

                File.WriteAllText(filePath, sb.ToString());
#if USE_DEBUG
                GD.Print($"[StressTestManager] ✅ Results exported to: {filePath}");
#endif // USE_DEBUG
            }
            catch (Exception ex)
            {
#if USE_DEBUG
                GD.PrintErr($"[StressTestManager] Failed to export results: {ex.Message}");
#endif // USE_DEBUG
            }
        }

        /// <summary>
        /// Exports test results to CSV format for analysis.
        /// </summary>
        public void ExportCSV(string filePath)
        {
            if (completedResults.Count == 0)
            {
                GD.Print($"[StressTestManager] ⚠️ No results to export (0 completed tests)");
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
                string fullPath = System.IO.Path.GetFullPath(filePath);
                GD.Print($"");
                GD.Print($"╔════════════════════════════════════════════════════════════");
                GD.Print($"║ ✅ CSV EXPORTED");
                GD.Print($"╠════════════════════════════════════════════════════════════");
                GD.Print($"║ Results: {completedResults.Count} tests");
                GD.Print($"║ Frames:  {totalFrames}");
                GD.Print($"║ Path:    {fullPath}");
                GD.Print($"╚════════════════════════════════════════════════════════════");
                GD.Print($"");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"");
                GD.PrintErr($"╔════════════════════════════════════════════════════════════");
                GD.PrintErr($"║ ❌ CSV EXPORT FAILED");
                GD.PrintErr($"╠════════════════════════════════════════════════════════════");
                GD.PrintErr($"║ Path:  {filePath}");
                GD.PrintErr($"║ Error: {ex.Message}");
                GD.PrintErr($"╚════════════════════════════════════════════════════════════");
                GD.PrintErr($"");
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

#if USE_DEBUG
            GD.Print("[StressTestManager] Stopped");
#endif // USE_DEBUG
        }

        /// <summary>
        /// Clears all completed results.
        /// </summary>
        public void ClearResults()
        {
            completedResults.Clear();
            totalFrames = 0;
            csvExportedAt1000 = false;
#if USE_DEBUG
            GD.Print("[StressTestManager] Results cleared");
#endif // USE_DEBUG
        }
        
        /// <summary>
        /// Call this on application quit to ensure CSV export.
        /// </summary>
        public void OnApplicationQuit()
        {
            if (completedResults.Count > 0)
            {
                string path = GetExportPath($"stress_test_quit_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                ExportCSV(path);
            }
        }
    }
}