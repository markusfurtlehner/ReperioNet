namespace ReperioNet;

/// <summary>
/// Thrown for ReperioNet-specific failures: a missing FTS5 module, an SQLite engine older than 3.43.0,
/// a corrupt or incompatible schema, a layout-flag mismatch on reopen, or a missing
/// <c>ReperioOptions&lt;TMeta&gt;.MetadataTypeInfo</c>.
/// </summary>
public class ReperioException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ReperioException"/> class.</summary>
    public ReperioException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ReperioException"/> class with a message.</summary>
    /// <param name="message">The error message.</param>
    public ReperioException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ReperioException"/> class with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public ReperioException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
