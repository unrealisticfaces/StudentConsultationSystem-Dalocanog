using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StudentConsultationSystem
{
    // ==========================================
    // 1. DATA MODEL
    // ==========================================
    public class ConsultationRecord
    {
        public string RecordId { get; set; }
        // 4 Domain Fields
        public string StudentName { get; set; }
        public string Subject { get; set; }
        public string Topic { get; set; }
        public string Notes { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsActive { get; set; }
        public string Checksum { get; set; }

        public void UpdateChecksum()
        {
            string rawData = $"{RecordId}|{StudentName}|{Subject}|{Topic}|{Notes}|{CreatedAt.Ticks}|{UpdatedAt.Ticks}|{IsActive}";
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                Checksum = Convert.ToBase64String(bytes);
            }
        }

        public bool IsValidChecksum()
        {
            string currentChecksum = Checksum;
            UpdateChecksum();
            bool isValid = currentChecksum == Checksum;
            Checksum = currentChecksum; // Restore original just in case
            return isValid;
        }

        // Serialization helpers
        public string ToDelimitedString()
        {
            return $"{RecordId}|{StudentName}|{Subject}|{Topic}|{Notes}|{CreatedAt:O}|{UpdatedAt:O}|{IsActive}|{Checksum}";
        }

        public static ConsultationRecord FromDelimitedString(string line)
        {
            var parts = line.Split('|');
            if (parts.Length < 9) throw new FormatException("Malformed record line.");

            return new ConsultationRecord
            {
                RecordId = parts[0],
                StudentName = parts[1],
                Subject = parts[2],
                Topic = parts[3],
                Notes = parts[4],
                CreatedAt = DateTime.Parse(parts[5]),
                UpdatedAt = DateTime.Parse(parts[6]),
                IsActive = bool.Parse(parts[7]),
                Checksum = parts[8]
            };
        }
    }

    // ==========================================
    // 2. AUDIT LOGGER
    // ==========================================
    public static class AuditLogger
    {
        private static readonly string LogFilePath = Path.Combine("Data", "audit.log");

        public static void Log(string action, string details)
        {
            try
            {
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{action.ToUpper()}] {details}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, logEntry);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FATAL] Failed to write to audit log: {ex.Message}");
            }
        }
    }

    // ==========================================
    // 3. VALIDATION COMPONENT
    // ==========================================
    public static class ValidationComponent
    {
        public static string Sanitize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "N/A";
            // Remove pipe characters and newlines to prevent file corruption
            return input.Replace("|", "-").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        public static bool ValidateRecord(ConsultationRecord record, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(record.StudentName) || record.StudentName == "N/A")
            {
                errorMessage = "Student Name cannot be empty.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(record.Subject) || record.Subject == "N/A")
            {
                errorMessage = "Subject cannot be empty.";
                return false;
            }
            errorMessage = string.Empty;
            return true;
        }
    }

    // ==========================================
    // 4. FILE REPOSITORY
    // ==========================================
    public class FileRepository
    {
        private readonly string DataDirectory = "Data";
        private readonly string RecordsFilePath = Path.Combine("Data", "records.txt");

        public void InitializeStorage()
        {
            try
            {
                if (!Directory.Exists(DataDirectory))
                {
                    Directory.CreateDirectory(DataDirectory);
                    AuditLogger.Log("System", "Created Data directory.");
                }
                if (!File.Exists(RecordsFilePath))
                {
                    File.Create(RecordsFilePath).Dispose();
                    AuditLogger.Log("System", "Created records.txt file.");
                }
            }
            catch (Exception ex)
            {
                AuditLogger.Log("Error", $"Storage initialization failed: {ex.Message}");
                Console.WriteLine($"Error initializing storage: {ex.Message}");
            }
        }

        public List<ConsultationRecord> GetAllRecords()
        {
            var records = new List<ConsultationRecord>();
            try
            {
                string[] lines = File.ReadAllLines(RecordsFilePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var record = ConsultationRecord.FromDelimitedString(line);
                        if (!record.IsValidChecksum())
                        {
                            AuditLogger.Log("Warning", $"Record {record.RecordId} has invalid checksum. Data may be corrupted.");
                        }
                        records.Add(record);
                    }
                    catch (Exception ex)
                    {
                        AuditLogger.Log("Error", $"Failed to parse record line: {ex.Message}");
                    }
                }
                AuditLogger.Log("Read", $"Loaded {records.Count} records from file.");
            }
            catch (Exception ex)
            {
                AuditLogger.Log("Error", $"Failed to read records: {ex.Message}");
            }
            return records;
        }

        public void SaveAllRecords(List<ConsultationRecord> records)
        {
            try
            {
                var lines = records.Select(r => r.ToDelimitedString());
                File.WriteAllLines(RecordsFilePath, lines);
            }
            catch (Exception ex)
            {
                AuditLogger.Log("Error", $"Failed to save records: {ex.Message}");
                throw;
            }
        }

        public void AddRecord(ConsultationRecord record)
        {
            var records = GetAllRecords();
            records.Add(record);
            SaveAllRecords(records);
            AuditLogger.Log("Add", $"Added new record for {record.StudentName} (ID: {record.RecordId})");
        }

        public bool UpdateRecord(ConsultationRecord updatedRecord)
        {
            var records = GetAllRecords();
            var index = records.FindIndex(r => r.RecordId == updatedRecord.RecordId);
            if (index == -1) return false;

            records[index] = updatedRecord;
            SaveAllRecords(records);
            AuditLogger.Log("Update", $"Updated record {updatedRecord.RecordId}");
            return true;
        }

        public bool DeleteRecord(string recordId, bool hardDelete)
        {
            var records = GetAllRecords();
            var record = records.FirstOrDefault(r => r.RecordId == recordId);
            if (record == null) return false;

            if (hardDelete)
            {
                records.Remove(record);
                AuditLogger.Log("Delete_Hard", $"Permanently removed record {recordId}");
            }
            else
            {
                record.IsActive = false;
                record.UpdatedAt = DateTime.Now;
                record.UpdateChecksum();
                AuditLogger.Log("Delete_Soft", $"Soft deleted record {recordId}");
            }

            SaveAllRecords(records);
            return true;
        }
    }

    // ==========================================
    // 5. REPORT GENERATOR
    // ==========================================
    public class ReportGenerator
    {
        private readonly FileRepository _repository;
        public ReportGenerator(FileRepository repository)
        {
            _repository = repository;
        }

        public void GenerateSubjectSummaryReport()
        {
            try
            {
                var records = _repository.GetAllRecords().Where(r => r.IsActive).ToList();
                string reportPath = Path.Combine("Data", $"Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                var grouped = records.GroupBy(r => r.Subject)
                                     .Select(g => new { Subject = g.Key, Count = g.Count() })
                                     .OrderByDescending(x => x.Count);

                using (StreamWriter sw = new StreamWriter(reportPath))
                {
                    sw.WriteLine("========================================");
                    sw.WriteLine("  ACTIVE CONSULTATIONS SUMMARY REPORT   ");
                    sw.WriteLine($"  Generated: {DateTime.Now}           ");
                    sw.WriteLine("========================================");
                    sw.WriteLine($"Total Active Consultations: {records.Count}\n");

                    foreach (var item in grouped)
                    {
                        sw.WriteLine($"- {item.Subject}: {item.Count} consultation(s)");
                    }
                }

                Console.WriteLine($"\nReport successfully generated at: {reportPath}");
                AuditLogger.Log("Report", $"Generated Subject Summary Report at {reportPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to generate report: {ex.Message}");
                AuditLogger.Log("Error", $"Report generation failed: {ex.Message}");
            }
        }
    }

    // ==========================================
    // 6. PROGRAM CONTROLLER (Menu & Loop)
    // ==========================================
    class Program
    {
        static FileRepository _repository = new FileRepository();
        static ReportGenerator _reportGenerator = new ReportGenerator(_repository);

        static void Main(string[] args)
        {
            _repository.InitializeStorage();
            bool running = true;

            while (running)
            {
                Console.Clear();
                Console.WriteLine("=== STUDENT CONSULTATION LOGS ===");
                Console.WriteLine("1. Add Record");
                Console.WriteLine("2. View/Search Records");
                Console.WriteLine("3. Update Record");
                Console.WriteLine("4. Delete Record (Soft)");
                Console.WriteLine("5. Delete Record (Hard)");
                Console.WriteLine("6. Generate Report");
                Console.WriteLine("7. Exit");
                Console.Write("\nSelect an option: ");

                string choice = Console.ReadLine();
                Console.WriteLine();

                switch (choice)
                {
                    case "1": AddRecord(); break;
                    case "2": ViewRecords(); break;
                    case "3": UpdateRecord(); break;
                    case "4": DeleteRecord(hardDelete: false); break;
                    case "5": DeleteRecord(hardDelete: true); break;
                    case "6": _reportGenerator.GenerateSubjectSummaryReport(); Pause(); break;
                    case "7": running = false; break;
                    default: Console.WriteLine("Invalid option. Try again."); Pause(); break;
                }
            }

            AuditLogger.Log("System", "Application exited normally.");
        }

        static void AddRecord()
        {
            Console.WriteLine("--- ADD NEW CONSULTATION ---");
            var record = new ConsultationRecord
            {
                RecordId = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(),
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                IsActive = true
            };

            Console.Write("Student Name: ");
            record.StudentName = ValidationComponent.Sanitize(Console.ReadLine());

            Console.Write("Subject (e.g., Math, CS101): ");
            record.Subject = ValidationComponent.Sanitize(Console.ReadLine());

            Console.Write("Topic: ");
            record.Topic = ValidationComponent.Sanitize(Console.ReadLine());

            Console.Write("Notes: ");
            record.Notes = ValidationComponent.Sanitize(Console.ReadLine());

            if (ValidationComponent.ValidateRecord(record, out string error))
            {
                record.UpdateChecksum();
                _repository.AddRecord(record);
                Console.WriteLine($"\nRecord saved successfully! Assigned ID: {record.RecordId}");
            }
            else
            {
                Console.WriteLine($"\nValidation Failed: {error}");
                AuditLogger.Log("Warning", $"Failed add attempt. Reason: {error}");
            }
            Pause();
        }

        static void ViewRecords()
        {
            Console.WriteLine("--- VIEW RECORDS ---");
            Console.Write("Search by Student Name (leave empty to show all active): ");
            string query = Console.ReadLine()?.Trim();

            var records = _repository.GetAllRecords()
                                     .Where(r => r.IsActive)
                                     .ToList();

            if (!string.IsNullOrEmpty(query))
            {
                records = records.Where(r => r.StudentName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!records.Any())
            {
                Console.WriteLine("\nNo matching active records found.");
            }
            else
            {
                Console.WriteLine("\nID       | Name               | Subject    | Topic                | Created");
                Console.WriteLine(new string('-', 80));
                foreach (var r in records)
                {
                    Console.WriteLine($"{r.RecordId,-8} | {r.StudentName,-18} | {r.Subject,-10} | {r.Topic,-20} | {r.CreatedAt:yyyy-MM-dd}");
                }
            }
            Pause();
        }

        static void UpdateRecord()
        {
            Console.WriteLine("--- UPDATE RECORD ---");
            Console.Write("Enter Record ID to update: ");
            string id = Console.ReadLine()?.Trim().ToUpper();

            var records = _repository.GetAllRecords();
            var record = records.FirstOrDefault(r => r.RecordId == id && r.IsActive);

            if (record == null)
            {
                Console.WriteLine("Record not found or is inactive.");
                Pause();
                return;
            }

            Console.WriteLine($"\nUpdating Record for {record.StudentName}");
            Console.WriteLine("Press Enter to keep current value.");

            Console.Write($"Subject [{record.Subject}]: ");
            string subj = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(subj)) record.Subject = ValidationComponent.Sanitize(subj);

            Console.Write($"Topic [{record.Topic}]: ");
            string topic = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(topic)) record.Topic = ValidationComponent.Sanitize(topic);

            Console.Write($"Notes [{record.Notes}]: ");
            string notes = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(notes)) record.Notes = ValidationComponent.Sanitize(notes);

            if (ValidationComponent.ValidateRecord(record, out string error))
            {
                record.UpdatedAt = DateTime.Now;
                record.UpdateChecksum();
                _repository.UpdateRecord(record);
                Console.WriteLine("\nRecord updated successfully.");
            }
            else
            {
                Console.WriteLine($"\nValidation Failed: {error}");
            }
            Pause();
        }

        static void DeleteRecord(bool hardDelete)
        {
            Console.WriteLine($"--- {(hardDelete ? "HARD" : "SOFT")} DELETE RECORD ---");
            Console.Write("Enter Record ID to delete: ");
            string id = Console.ReadLine()?.Trim().ToUpper();

            bool success = _repository.DeleteRecord(id, hardDelete);

            if (success)
            {
                Console.WriteLine("\nRecord successfully deleted.");
            }
            else
            {
                Console.WriteLine("\nRecord not found.");
            }
            Pause();
        }

        static void Pause()
        {
            Console.WriteLine("\nPress any key to return to menu...");
            Console.ReadKey();
        }
    }
}