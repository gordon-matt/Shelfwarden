using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

/// <summary>
/// Generated audiobook for a <see cref="Book"/>. Created when a user clicks
/// "Generate Audio" on the book detail page; updated by the TTS Hangfire job as it
/// progresses. There is at most one row per book — regenerating reuses the same row.
/// </summary>
public class Audiobook : BaseEntity<int>
{
    public int BookId { get; set; }

    public AudiobookStatus Status { get; set; } = AudiobookStatus.Pending;

    /// <summary>The Kokoro voice name used for synthesis (e.g. "af_heart").</summary>
    public required string VoiceName { get; set; }

    /// <summary>
    /// Filename (relative to the audiobooks directory) of the final encoded file once
    /// generation succeeds. Null while pending / running / failed.
    /// </summary>
    public string? OutputFileName { get; set; }

    /// <summary>Total chunks the source text was broken into. 0 until chunking finishes.</summary>
    public int TotalChunks { get; set; }

    /// <summary>How many chunks have been synthesised so far. Drives the progress bar.</summary>
    public int CompletedChunks { get; set; }

    /// <summary>Bytes of the final encoded file once generation succeeds.</summary>
    public long? OutputSizeBytes { get; set; }

    /// <summary>Total runtime in seconds. Set when the job finishes (success or failure).</summary>
    public double? DurationSeconds { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>The user who originally requested this generation (used purely for audit/logging).</summary>
    public required string RequestedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public virtual Book Book { get; set; } = null!;
}

public enum AudiobookStatus
{
    /// <summary>Queued in Hangfire but not yet picked up by a worker.</summary>
    Pending = 0,

    /// <summary>The TTS worker is actively generating chunks.</summary>
    Running = 1,

    /// <summary>The encoded audiobook file is on disk.</summary>
    Completed = 2,

    /// <summary>Generation failed; <see cref="Audiobook.ErrorMessage"/> has the reason.</summary>
    Failed = 3,
}

public class AudiobookMap : IEntityTypeConfiguration<Audiobook>
{
    public void Configure(EntityTypeBuilder<Audiobook> builder)
    {
        builder.ToTable("Audiobooks", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.VoiceName).IsRequired().HasMaxLength(64);
        builder.Property(m => m.OutputFileName).HasMaxLength(256);
        builder.Property(m => m.RequestedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(m => m.ErrorMessage).HasMaxLength(2048).IsUnicode(true);

        builder.HasIndex(m => m.BookId).IsUnique();

        builder.HasOne(m => m.Book)
            .WithMany()
            .HasForeignKey(m => m.BookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
