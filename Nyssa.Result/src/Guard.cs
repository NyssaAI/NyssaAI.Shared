using System.Text.RegularExpressions;

namespace NyssaAI.Results;

/// <summary>Guard provides result-returning validation with explicit opt-in throwing through Result.ThrowIfError.</summary>
public static class Guard
{
    /// <summary>Against rejects the invalid condition named by each method; typed successes carry the validated value.</summary>
    public static class Against
    {
        /// <summary>Rejects a null reference, carrying the non-null value on success.</summary>
        /// <param name="value">The value to check.</param>
        /// <param name="parameterName">The field name to name in both messages.</param>
        /// <typeparam name="T">The reference type being checked.</typeparam>
        public static Result<T> Null<T>(T? value, string parameterName = "value") where T : class =>
            value is null
                ? Result<T>.Failure(Invalid($"{parameterName} cannot be null", $"{parameterName} is null"))
                : Result<T>.Success(value);

        /// <summary>Rejects a null nullable value, carrying the underlying value on success.</summary>
        /// <param name="value">The nullable value to check.</param>
        /// <param name="parameterName">The parameter named in the failure messages.</param>
        /// <typeparam name="T">The underlying value type.</typeparam>
        public static Result<T> Null<T>(T? value, string parameterName = "value") where T : struct =>
            value.HasValue
                ? Result<T>.Success(value.Value)
                : Result<T>.Failure(Invalid($"{parameterName} cannot be null", $"{parameterName} is null"));

        /// <summary>Validates that a string is neither null nor empty.</summary>
        /// <param name="value">The value to check.</param>
        /// <param name="parameterName">The field name to name in both messages.</param>
        public static Result<string> NullOrEmpty(string? value, string parameterName = "value") =>
            string.IsNullOrEmpty(value)
                ? Result<string>.Failure(Invalid($"{parameterName} cannot be empty", $"{parameterName} is null or empty"))
                : Result<string>.Success(value);

        /// <summary>Validates that a string is neither null nor entirely whitespace.</summary>
        /// <param name="value">The value to check.</param>
        /// <param name="parameterName">The field name to name in both messages.</param>
        public static Result<string> NullOrWhiteSpace(string? value, string parameterName = "value") =>
            string.IsNullOrWhiteSpace(value)
                ? Result<string>.Failure(Invalid($"{parameterName} cannot be empty", $"{parameterName} is null or whitespace"))
                : Result<string>.Success(value);

        /// <summary>Validates that a collection is neither null nor empty, returning it materialized.</summary>
        /// <param name="collection">The collection to check.</param>
        /// <param name="parameterName">The field name to name in both messages.</param>
        /// <typeparam name="T">The element type.</typeparam>
        public static Result<IReadOnlyList<T>> NullOrEmpty<T>(
            IEnumerable<T>? collection,
            string parameterName = "collection")
        {
            if (collection is null)
                return Result<IReadOnlyList<T>>.Failure(Invalid($"{parameterName} cannot be empty", $"{parameterName} is null or empty"));

            // Enumerating a caller's sequence runs the caller's code; a throw there is a failure, not
            // an exception escaping a guard.
            var materialized = Result.Try(collection.ToList);
            if (materialized.IsFailure)
                return Result<IReadOnlyList<T>>.Failure(materialized.Errors);

            return materialized.Value.Count == 0
                ? Result<IReadOnlyList<T>>.Failure(
                    Invalid($"{parameterName} cannot be empty", $"{parameterName} is null or empty"))
                : Result<IReadOnlyList<T>>.Success(materialized.Value);
        }

        /// <summary>Rejects a collection containing any item matching the predicate, carrying the original collection otherwise.</summary>
        /// <param name="collection">The collection to inspect; null is a Validation failure.</param>
        /// <param name="predicate">The matching rule; null is a Validation failure.</param>
        /// <param name="message">Message safe to display to end users.</param>
        /// <param name="technicalMessage">Diagnostic message for a match; defaults to the user message.</param>
        /// <param name="category">Failure category for a match, not for null arguments.</param>
        /// <typeparam name="TCollection">The original reference collection type, preserved on success.</typeparam>
        /// <typeparam name="TItem">The element type; use a typed lambda parameter or explicit type arguments for inference.</typeparam>
        /// <remarks>Short-circuits on a match without copying. Caller exceptions become failures through Result.Try. A lazy or single-use source is consumed up to the decision; the returned source is not a snapshot.</remarks>
        public static Result<TCollection> Contains<TCollection, TItem>(TCollection? collection, Func<TItem, bool>? predicate,
            string message, string? technicalMessage = null, ErrorCategory category = ErrorCategory.Validation)
            where TCollection : class, IEnumerable<TItem> =>
            CollectionMatch(collection, predicate, true, message, technicalMessage, category);

        /// <summary>Rejects a collection with no item matching the predicate, carrying the original collection otherwise.</summary>
        /// <param name="collection">The collection to inspect; null is a Validation failure.</param>
        /// <param name="predicate">The matching rule; null is a Validation failure.</param>
        /// <param name="message">Message safe to display to end users.</param>
        /// <param name="technicalMessage">Diagnostic message for a missing match; defaults to the user message.</param>
        /// <param name="category">Failure category for a missing match, not for null arguments.</param>
        /// <typeparam name="TCollection">The original reference collection type, preserved on success.</typeparam>
        /// <typeparam name="TItem">The element type; use a typed lambda parameter or explicit type arguments for inference.</typeparam>
        /// <remarks>Short-circuits on a match without copying. Caller exceptions become failures through Result.Try. A lazy or single-use source is consumed up to the decision; the returned source is not a snapshot.</remarks>
        public static Result<TCollection> NotContains<TCollection, TItem>(TCollection? collection, Func<TItem, bool>? predicate,
            string message, string? technicalMessage = null, ErrorCategory category = ErrorCategory.Validation)
            where TCollection : class, IEnumerable<TItem> =>
            CollectionMatch(collection, predicate, false, message, technicalMessage, category);

        private static Result<TCollection> CollectionMatch<TCollection, TItem>(TCollection? collection, Func<TItem, bool>? predicate,
            bool rejectMatch, string message, string? technicalMessage, ErrorCategory category)
            where TCollection : class, IEnumerable<TItem>
        {
            if (collection is null) return Result<TCollection>.Failure(Invalid(message, "The collection cannot be null."));
            if (predicate is null) return Result<TCollection>.Failure(Invalid(message, "The predicate cannot be null."));

            var matched = Result.Try(() => collection.Any(predicate));
            if (matched.IsFailure) return Result<TCollection>.Failure(matched.Errors);
            return matched.Value == rejectMatch
                ? Result<TCollection>.Failure(Error.FromMessages(message, technicalMessage ?? message, category))
                : Result<TCollection>.Success(collection);
        }

        /// <summary>Validates that a value falls within an inclusive range.</summary>
        /// <param name="value">The value to check.</param>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The inclusive upper bound.</param>
        /// <param name="parameterName">The field name to name in both messages.</param>
        /// <typeparam name="T">The comparable type being checked.</typeparam>
        public static Result<T> OutOfRange<T>(T value, T min, T max, string parameterName = "value")
            where T : IComparable<T>
        {
            if (value is null)
                return Result<T>.Failure(Invalid($"{parameterName} must be between {min} and {max}", $"{parameterName} is null"));

            return value.CompareTo(min) < 0 || value.CompareTo(max) > 0
                ? Result<T>.Failure(Invalid($"{parameterName} must be between {min} and {max}", $"{parameterName} value {value} is outside range [{min}, {max}]"))
                : Result<T>.Success(value);
        }

        /// <summary>Validates that a value is strictly greater than a lower bound.</summary>
        /// <param name="value">The value to check.</param>
        /// <param name="min">The exclusive lower bound.</param>
        /// <param name="parameterName">The field name to name in both messages.</param>
        /// <typeparam name="T">The comparable type being checked.</typeparam>
        public static Result<T> LessThanOrEqual<T>(T value, T min, string parameterName = "value")
            where T : IComparable<T>
        {
            if (value is null)
                return Result<T>.Failure(Invalid($"{parameterName} must be greater than {min}", $"{parameterName} is null"));

            return value.CompareTo(min) <= 0
                ? Result<T>.Failure(Invalid($"{parameterName} must be greater than {min}", $"{parameterName} value {value} is not greater than {min}"))
                : Result<T>.Success(value);
        }

        /// <summary>Validates that a value is greater than or equal to a lower bound.</summary>
        /// <param name="value">The value to check.</param>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="parameterName">The field name to name in both messages.</param>
        /// <typeparam name="T">The comparable type being checked.</typeparam>
        public static Result<T> LessThan<T>(T value, T min, string parameterName = "value")
            where T : IComparable<T>
        {
            if (value is null)
                return Result<T>.Failure(Invalid($"{parameterName} must be greater than or equal to {min}", $"{parameterName} is null"));

            return value.CompareTo(min) < 0
                ? Result<T>.Failure(Invalid($"{parameterName} must be greater than or equal to {min}", $"{parameterName} value {value} is not >= {min}"))
                : Result<T>.Success(value);
        }

        /// <summary>Rejects a value greater than the inclusive maximum, or a null reference.</summary>
        /// <param name="value">The value to check.</param>
        /// <param name="max">The inclusive upper bound.</param>
        /// <param name="parameterName">The parameter named in the failure messages.</param>
        /// <typeparam name="T">The comparable type.</typeparam>
        public static Result<T> GreaterThan<T>(T value, T max, string parameterName = "value") where T : IComparable<T>
        {
            if (value is null) return Result<T>.Failure(Invalid($"{parameterName} cannot be null", $"{parameterName} is null"));
            return value.CompareTo(max) > 0
                ? Result<T>.Failure(Invalid($"{parameterName} must be less than or equal to {max}", $"{parameterName} value {value} exceeds {max}"))
                : Result<T>.Success(value);
        }

        /// <summary>Rejects a value greater than or equal to the exclusive maximum, or a null reference.</summary>
        /// <param name="value">The value to check.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <param name="parameterName">The parameter named in the failure messages.</param>
        /// <typeparam name="T">The comparable type.</typeparam>
        public static Result<T> GreaterThanOrEqual<T>(T value, T max, string parameterName = "value") where T : IComparable<T>
        {
            if (value is null) return Result<T>.Failure(Invalid($"{parameterName} cannot be null", $"{parameterName} is null"));
            return value.CompareTo(max) >= 0
                ? Result<T>.Failure(Invalid($"{parameterName} must be less than {max}", $"{parameterName} value {value} is not less than {max}"))
                : Result<T>.Success(value);
        }

        /// <summary>Rejects null strings, invalid length bounds, and lengths outside the inclusive range.</summary>
        /// <param name="value">The string to check.</param>
        /// <param name="minLength">The nonnegative inclusive minimum length.</param>
        /// <param name="maxLength">The inclusive maximum, at least the minimum.</param>
        /// <param name="parameterName">The parameter named in the failure messages.</param>
        public static Result<string> LengthOutOfRange(string? value, int minLength, int maxLength, string parameterName = "value")
        {
            if (value is null) return Result<string>.Failure(Invalid($"{parameterName} cannot be null", $"{parameterName} is null"));
            if (minLength < 0 || maxLength < minLength) return Result<string>.Failure(Invalid($"{parameterName} length bounds are invalid", "Length bounds must be nonnegative and ordered"));
            return value.Length < minLength || value.Length > maxLength
                ? Result<string>.Failure(Invalid($"{parameterName} must be between {minLength} and {maxLength} characters", $"{parameterName} length {value.Length} is outside range [{minLength}, {maxLength}]"))
                : Result<string>.Success(value);
        }

        /// <summary>Rejects a string that does not match a regular expression, with bounded backtracking.</summary>
        /// <param name="value">The value to check.</param>
        /// <param name="pattern">The regular expression the value must match.</param>
        /// <param name="parameterName">The field name to name in both messages.</param>
        public static Result<string> PatternMismatch(string? value, string pattern, string parameterName = "value")
        {
            if (value is null)
            {
                return Result<string>.Failure(Invalid($"{parameterName} format is invalid", $"{parameterName} is null"));
            }

            // The pattern is caller-supplied, so the backtracking budget is bounded here rather than
            // left to run away: a pattern like (a+)+$ against a long non-matching input would otherwise
            // consume a core indefinitely. A timeout is a failure, not an exception, like every other
            // outcome this type produces.
            try
            {
                return Regex.IsMatch(value, pattern, RegexOptions.None, MatchTimeout)
                    ? Result<string>.Success(value)
                    : Result<string>.Failure(
                        Invalid($"{parameterName} format is invalid", $"{parameterName} value '{value}' does not match pattern '{pattern}'"));
            }
            catch (RegexMatchTimeoutException)
            {
                return Result<string>.Failure(Error.FromMessages(
                    $"{parameterName} could not be checked.",
                    $"{parameterName} pattern '{pattern}' exceeded the {MatchTimeout.TotalMilliseconds:F0}ms match timeout",
                    ErrorCategory.Unavailable));
            }
            catch (ArgumentException exception)
            {
                return Result<string>.Failure(Invalid(
                    $"{parameterName} could not be checked.",
                    $"{parameterName} pattern '{pattern}' is not a valid regular expression: {exception.Message}"));
            }
        }

        /// <summary>Rejects a true condition.</summary>
        /// <param name="condition">The invalid condition.</param>
        /// <param name="message">Message safe to display to end users.</param>
        /// <param name="technicalMessage">Diagnostic message; defaults to the user message.</param>
        /// <param name="category">The kind of failure.</param>
        public static Result True(bool condition, string message, string? technicalMessage = null, ErrorCategory category = ErrorCategory.Validation) =>
            False(!condition, message, technicalMessage, category);

        /// <summary>Rejects a value when its predicate is true, carrying the value otherwise.</summary>
        /// <param name="value">The value to check and carry.</param>
        /// <param name="predicate">The invalid condition for the value.</param>
        /// <param name="message">Message safe to display to end users.</param>
        /// <param name="technicalMessage">Diagnostic message; defaults to the user message.</param>
        /// <param name="category">The kind of failure.</param>
        /// <typeparam name="T">The type of the value.</typeparam>
        public static Result<T> True<T>(T value, Func<T, bool> predicate, string message, string? technicalMessage = null, ErrorCategory category = ErrorCategory.Validation) =>
            False(value, candidate => !predicate(candidate), message, technicalMessage, category);

        /// <summary>Rejects a false condition.</summary>
        /// <param name="condition">The condition that must hold.</param>
        /// <param name="message">Message safe to display to end users.</param>
        /// <param name="technicalMessage">Diagnostic message; defaults to the user message.</param>
        /// <param name="category">The kind of failure; validation unless the rule says otherwise.</param>
        public static Result False(
            bool condition,
            string message,
            string? technicalMessage = null,
            ErrorCategory category = ErrorCategory.Validation) =>
            condition
                ? Result.Success()
                : Result.Failure(Error.FromMessages(message, technicalMessage ?? message, category));

        /// <summary>Rejects a value when its predicate is false, carrying the value otherwise.</summary>
        /// <param name="value">The value to check and carry.</param>
        /// <param name="predicate">The condition that must hold for the value.</param>
        /// <param name="message">Message safe to display to end users.</param>
        /// <param name="technicalMessage">Diagnostic message; defaults to the user message.</param>
        /// <param name="category">The kind of failure; validation unless the rule says otherwise.</param>
        /// <typeparam name="T">The type of the value being checked.</typeparam>
        public static Result<T> False<T>(
            T value,
            Func<T, bool> predicate,
            string message,
            string? technicalMessage = null,
            ErrorCategory category = ErrorCategory.Validation)
        {
            // The predicate is caller code; a throw there is a failure, not an exception escaping a guard.
            var verdict = Result.Try(() => predicate(value));
            if (verdict.IsFailure)
                return Result<T>.Failure(verdict.Errors);

            return verdict.Value
                ? Result<T>.Success(value)
                : Result<T>.Failure(Error.FromMessages(message, technicalMessage ?? message, category));
        }

        // How long a single PatternMismatch call may spend backtracking before it is abandoned.
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

        // Every guard failure is a validation failure by definition: input did not satisfy a
        // precondition. Routing them through one helper is what keeps that from drifting back to the
        // default category, which is Unexpected and means "we do not know what went wrong".
        private static Error Invalid(string userMessage, string technicalMessage) =>
            Error.FromMessages(userMessage, technicalMessage, ErrorCategory.Validation);
    }
}
