using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Data
{
    public static class GenericValueComparer
    {
        public static ValueComparer<T> Create<T>()
        {
            return typeof(T).IsGenericType &&
                   typeof(T).GetGenericTypeDefinition() == typeof(List<>)
                ? CreateListComparer<T>()
                : new ValueComparer<T>(
                    (a, b) => EqualityComparer<T>.Default.Equals(a, b),
                    a => a == null ? 0 : EqualityComparer<T>.Default.GetHashCode(a),
                    a => a // snapshot delegate
                );
        }

        private static ValueComparer<T> CreateListComparer<T>()
        {
            var elementType = typeof(T).GetGenericArguments()[0];

            // T = List<U>
            return new ValueComparer<T>(
                (a, b) => SequenceEqual(a, b),
                a => GetSequenceHashCode(a),
                a => CloneList(a)
            );
        }

        private static bool SequenceEqual<T>(T? a, T? b)
        {
            if (a is IEnumerable<object> listA && b is IEnumerable<object> listB)
                return listA.SequenceEqual(listB);
            return EqualityComparer<T>.Default.Equals(a, b);
        }

        private static int GetSequenceHashCode<T>(T? list)
        {
            if (list is IEnumerable<object> sequence)
                return sequence.Aggregate(0, (hash, item) => HashCode.Combine(hash, item?.GetHashCode() ?? 0));
            return list?.GetHashCode() ?? 0;
        }

        private static T CloneList<T>(T? list)
        {
            if (list is IEnumerable<object> source && list is IList<object> original)
            {
                var cloned = Activator.CreateInstance(typeof(T)) as IList<object>;
                foreach (var item in original)
                    cloned?.Add(item);
                return (T)(object)cloned!;
            }

            return list!;
        }
    }

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
            Database.EnsureCreated();
        }

        public DbSet<Series> Series { get; set; }
        public DbSet<Setting> Settings { get; set; }
        public DbSet<SeriesProvider> SeriesProviders { get; set; }
        public DbSet<ProviderStorage> Providers { get; set; }
        public DbSet<Import> Imports { get; set; }
        public DbSet<EtagCache> ETagCache { get; set; }
        public DbSet<Job> Jobs { get; set; }
        public DbSet<Enqueue> Queues { get; set; }
        public DbSet<LatestSerie> LatestSeries { get; set; }

        


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Series configuration
            modelBuilder.Entity<Series>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.Title).UseCollation("BINARY").IsRequired();
                entity.Property(s => s.StoragePath).UseCollation("BINARY").IsRequired(false);
                entity.Property(s => s.Type).UseCollation("BINARY").IsRequired(false);
                entity.Property(s => s.ThumbnailUrl).UseCollation("BINARY").IsRequired();
                entity.Property(s => s.Artist).UseCollation("BINARY").IsRequired();
                entity.Property(s => s.Author).UseCollation("BINARY").IsRequired();
                entity.Property(s => s.Description).UseCollation("BINARY").IsRequired();
                entity.Property(s => s.Status).IsRequired();
                entity.Property(s => s.ChapterCount).IsRequired();
                entity.Property(s => s.PauseDownloads).IsRequired();
                // Configure JSON conversion for Genre list
                entity.Property(s => s.Genre)
                    .HasConversion(
                        v => string.Join(',', v),
                        v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                    ).Metadata.SetValueComparer(GenericValueComparer.Create<List<string>>());
                // Configure relationship with SeriesProvider
                entity.HasMany(s => s.Sources)
                    .WithOne()
                    .HasForeignKey(sp => sp.SeriesId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // SeriesProvider configuration
            modelBuilder.Entity<SeriesProvider>(entity =>
            {
                entity.HasKey(sp => sp.Id);
                entity.Property(sp => sp.Provider).UseCollation("BINARY").IsRequired();
                entity.Property(sp => sp.Url).UseCollation("BINARY").IsRequired(false);
                entity.Property(sp => sp.Title).UseCollation("BINARY").IsRequired();
                entity.Property(sp => sp.Language).UseCollation("BINARY").IsRequired();
                entity.Property(sp => sp.Scanlator).UseCollation("BINARY").IsRequired();
                entity.Property(sp => sp.ThumbnailUrl).UseCollation("BINARY").IsRequired(false);
                entity.Property(sp => sp.Artist).UseCollation("BINARY").IsRequired(false);
                entity.Property(sp => sp.Author).UseCollation("BINARY").IsRequired(false);
                entity.Property(sp => sp.Description).UseCollation("BINARY").IsRequired(false);
                entity.Property(sp => sp.FetchDate).IsRequired(false);
                entity.Property(sp => sp.ChapterCount).IsRequired(false);
                entity.Property(sp => sp.ContinueAfterChapter).IsRequired(false);
                entity.Property(sp => sp.IsTitle).IsRequired();
                entity.Property(sp => sp.IsCover).IsRequired();
                entity.Property(sp => sp.IsUnknown).IsRequired();
                entity.Property(sp => sp.IsStorage).IsRequired();
                entity.Property(sp => sp.IsDisabled).IsRequired();
                entity.Property(sp => sp.Status).IsRequired();
                // Configure JSON conversion for Genre list
                entity.Property(sp => sp.Genre)
                    .HasConversion(
                        v => string.Join(',', v),
                        v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                    ).Metadata.SetValueComparer(GenericValueComparer.Create<List<string>>());
                // Configure JSON conversion for Chapters list
                entity.Property(sp => sp.Chapters)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, new JsonSerializerOptions { WriteIndented = false }),
                        v => JsonSerializer.Deserialize<List<Chapter>>(v, new JsonSerializerOptions { WriteIndented = false }) ?? new List<Chapter>()
                    ).Metadata.SetValueComparer(GenericValueComparer.Create<List<Chapter>>());
                entity.HasIndex(sp => sp.SeriesId).HasDatabaseName("IX_SeriesProvider_SeriesId");
                entity.HasIndex(sp => sp.SuwayomiId).HasDatabaseName("IX_SeriesProvider_SuwayomiId");
                entity.HasIndex(sp => new { sp.Title, sp.Language })
                    .HasDatabaseName("IX_SeriesProvider_Title_Language");
                entity.HasIndex(sp => new { sp.Provider, sp.Language, sp.Scanlator })
                    .HasDatabaseName("IX_SeriesProvider_Provider_Language_Scanlator");
            });

            // LatestSerie configuration
            modelBuilder.Entity<LatestSerie>(entity =>
            {
                entity.HasKey(e => e.SuwayomiId);
                entity.Property(e => e.SuwayomiSourceId).UseCollation("BINARY").IsRequired();
                entity.Property(e => e.Provider).UseCollation("BINARY").IsRequired();
                entity.Property(e => e.LatestChapterTitle).UseCollation("BINARY").IsRequired();
                entity.Property(e => e.Language).UseCollation("BINARY").IsRequired();
                entity.Property(e => e.Url).UseCollation("BINARY").IsRequired(false);
                entity.Property(e => e.Title).IsRequired();
                entity.Property(e => e.ThumbnailUrl).UseCollation("BINARY").IsRequired(false);
                entity.Property(e => e.Artist).UseCollation("BINARY").IsRequired(false);
                entity.Property(e => e.Author).UseCollation("BINARY").IsRequired(false);
                entity.Property(e => e.Description).UseCollation("BINARY").IsRequired(false);
                entity.Property(e => e.FetchDate).IsRequired();
                entity.Property(e => e.LatestChapter).IsRequired(false);
                entity.Property(e => e.ChapterCount).IsRequired(false);
                entity.Property(e => e.Status).IsRequired();
                entity.Property(e => e.InLibrary).IsRequired();
                entity.Property(e => e.SeriesId).IsRequired(false);
                // Configure JSON conversion for Genre list
                entity.Property(e => e.Genre)
                    .HasConversion(
                        v => string.Join(',', v),
                        v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                    ).Metadata.SetValueComparer(GenericValueComparer.Create<List<string>>()); ;
                // Configure JSON conversion for Chapters list
                entity.Property(e => e.Chapters)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, new JsonSerializerOptions { WriteIndented = false }),
                        v => JsonSerializer.Deserialize<List<SuwayomiChapter>>(v, new JsonSerializerOptions { WriteIndented = false }) ?? new List<SuwayomiChapter>()
                    ).Metadata.SetValueComparer(GenericValueComparer.Create<List<SuwayomiChapter>>());
                entity.HasIndex(l => l.SuwayomiSourceId).HasDatabaseName("IX_LatestSerie_SuwayomiSourceId");
                entity.HasIndex(l => l.FetchDate).HasDatabaseName("IX_LatestSerie_FetchDate");
                entity.HasIndex(l => l.Title).HasDatabaseName("IX_LatestSerie_Title");
                entity.HasIndex(l => l.SuwayomiId).HasDatabaseName("IX_LatestSerie_SuwayomiId");
            });

            // ProviderStorage configuration
            modelBuilder.Entity<ProviderStorage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.Name, e.Lang });
                entity.Property(e => e.ApkName).UseCollation("BINARY").IsRequired();
                entity.Property(e => e.PkgName).UseCollation("BINARY").IsRequired();
                entity.Property(e => e.Name).UseCollation("BINARY").IsRequired();
                entity.Property(e => e.Lang).UseCollation("BINARY").IsRequired();
                entity.Property(e => e.VersionCode).IsRequired();
                entity.Property(e => e.IsStorage).IsRequired();
                entity.Property(e => e.IsDisabled).IsRequired();
                // Store Mappings as JSON
                entity.Property(e => e.Mappings)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, new JsonSerializerOptions { WriteIndented = false }),
                        v => JsonSerializer.Deserialize<List<Mappings>>(v, new JsonSerializerOptions { WriteIndented = false }) ?? new List<Mappings>()
                    ).Metadata.SetValueComparer(GenericValueComparer.Create<List<Mappings>>());
            });

            // Import configuration
            modelBuilder.Entity<Import>(entity =>
            {
                entity.HasKey(i => i.Path);
                entity.Property(i => i.Path).UseCollation("BINARY").IsRequired();
                entity.Property(i => i.Title).UseCollation("BINARY").IsRequired();
                // Configure ImportStatus enum
                entity.Property(i => i.Status)
                    .IsRequired()
                    .HasDefaultValue(ImportStatus.Import)
                    .HasConversion<int>();
                // Configure Action enum
                entity.Property(i => i.Action)
                    .IsRequired()
                    .HasDefaultValue(Models.Database.Action.Add)
                    .HasConversion<int>();
                // Configure JSON conversion for KaizokuInfo
                entity.Property(i => i.Info)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, new JsonSerializerOptions { WriteIndented = false }),
                        v => JsonSerializer.Deserialize<KaizokuInfo>(v, new JsonSerializerOptions { WriteIndented = false }) ?? new KaizokuInfo()
                    ).Metadata.SetValueComparer(GenericValueComparer.Create<KaizokuInfo>());
                // Configure JSON conversion for Series list (FullSeries)
                entity.Property(i => i.Series)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, new JsonSerializerOptions { WriteIndented = false }),
                        v => JsonSerializer.Deserialize<List<FullSeries>>(v, new JsonSerializerOptions { WriteIndented = false }) ?? new List<FullSeries>()
                    ).Metadata.SetValueComparer(GenericValueComparer.Create<List<FullSeries>>());
                entity.HasIndex(i => new { i.Status, i.Action })
                    .HasDatabaseName("IX_Import_Status_Action");
            });

            // EtagCache configuration
            modelBuilder.Entity<EtagCache>(entity =>
            {
                entity.HasKey(i => i.Key);
                entity.Property(i => i.Key).UseCollation("BINARY").IsRequired();
                entity.Property(i => i.Etag).UseCollation("BINARY").IsRequired();
                entity.Property(i => i.LastUpdated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.LastUpdated).HasDatabaseName("IX_ETagCache_LastUpdated");
            });

            // Setting configuration
            modelBuilder.Entity<Setting>(entity =>
            {
                entity.HasKey(s => s.Name);
                entity.Property(s => s.Name).UseCollation("BINARY").IsRequired();
                entity.Property(s => s.Value).UseCollation("BINARY").IsRequired();
            });

            // Job configuration
            modelBuilder.Entity<Job>(entity =>
            {
                entity.HasKey(j => j.Id);
                entity.Property(j => j.Key).UseCollation("BINARY").IsRequired();
                entity.Property(j => j.GroupKey).UseCollation("BINARY").IsRequired();
                entity.Property(j => j.JobParameters).UseCollation("BINARY").IsRequired();
                entity.Property(j => j.JobType)
                    .IsRequired()
                    .HasConversion<int>();
                entity.Property(j => j.Priority)
                    .IsRequired()
                    .HasDefaultValue(Priority.Low)
                    .HasConversion<int>();
                entity.Property(j => j.TimeBetweenJobs).IsRequired();
                entity.Property(j => j.MinutePlace).IsRequired();
                entity.Property(j => j.NextExecution).IsRequired();
                entity.Property(j => j.PreviousExecution).IsRequired(false);
                // Create an index on Key for faster lookups
                entity.HasIndex(j => j.Key);
                // Create an index on NextExecution for faster queries
                entity.HasIndex(j => j.NextExecution);
                entity.HasIndex(j => new { j.JobType, j.GroupKey })
                    .HasDatabaseName("IX_Job_JobType_GroupKey");
                entity.HasIndex(j => j.IsEnabled).HasDatabaseName("IX_Job_IsEnabled");
                entity.HasIndex(j => new { j.JobType, j.Key })
                    .HasDatabaseName("IX_Job_JobType_Key");
            });

            // Enqueue configuration
            modelBuilder.Entity<Enqueue>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Queue).UseCollation("BINARY").IsRequired();
                entity.Property(e => e.Key).UseCollation("BINARY").IsRequired();
                entity.Property(e => e.GroupKey).UseCollation("BINARY").IsRequired();
                entity.Property(e => e.ExtraKey).UseCollation("BINARY").IsRequired(false);
                entity.Property(e => e.JobParameters).UseCollation("BINARY").IsRequired(false);
                entity.Property(e => e.JobType)
                    .IsRequired()
                    .HasConversion<int>();
                entity.Property(e => e.Status)
                    .IsRequired()
                    .HasDefaultValue(QueueStatus.Waiting)
                    .HasConversion<int>();
                entity.Property(e => e.Priority)
                    .IsRequired()
                    .HasDefaultValue(Priority.Low)
                    .HasConversion<int>();
                entity.Property(e => e.EnqueuedDate).IsRequired();
                entity.Property(e => e.StartedDate).IsRequired(false);
                entity.Property(e => e.ScheduledDate).IsRequired();
                entity.Property(e => e.FinishedDate).IsRequired(false);
                entity.Property(e => e.RetryCount).IsRequired();
                // Create indexes for faster lookups
                entity.HasIndex(e => e.Queue);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.Key);
                entity.HasIndex(e => e.ScheduledDate);
                entity.HasIndex(e => new { e.JobType, e.Status })
                    .HasDatabaseName("IX_Enqueue_JobType_Status");
                entity.HasIndex(e => new { e.JobType, e.ExtraKey })
                    .HasDatabaseName("IX_Enqueue_JobType_ExtraKey");
                entity.HasIndex(e => e.GroupKey).HasDatabaseName("IX_Enqueue_GroupKey");
                entity.HasIndex(e => e.FinishedDate).HasDatabaseName("IX_Enqueue_FinishedDate");
            });
        }
    }
}
