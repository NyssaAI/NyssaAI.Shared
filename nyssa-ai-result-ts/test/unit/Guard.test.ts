import { describe, expect, it } from "vitest";
import { ErrorCategory } from "../../src/ErrorCategory.js";
import { Guard } from "../../src/Guard.js";

describe("Guard railway preconditions", () => {
  it("guard_nullAndNone_validatePresence", () => {
    // Arrange & Act
    const okRes = Guard.against.null("hello", "greeting");
    const failNull = Guard.againstNull<string>(null, "greeting");
    const failUndef = Guard.against.none<string>(undefined, "greeting");

    // Assert
    expect(okRes.ok).toBe(true);
    if (okRes.ok) {
      expect(okRes.value).toBe("hello");
    }
    expect(failNull.ok).toBe(false);
    expect(failNull.errors[0]?.category).toBe(ErrorCategory.Validation);
    expect(failNull.errors[0]?.userMessage).toContain("greeting cannot be null");
    expect(failUndef.ok).toBe(false);
  });

  it("guard_nullOrEmpty_validatesStringsAndArrays", () => {
    // Arrange & Act & Assert
    expect(Guard.against.nullOrEmpty("valid").ok).toBe(true);
    expect(Guard.againstEmpty("").ok).toBe(false);
    expect(Guard.against.nullOrEmpty<string>(null).ok).toBe(false);
    expect(Guard.against.nullOrEmpty([1, 2, 3]).ok).toBe(true);
    expect(Guard.against.nullOrEmpty([]).ok).toBe(false);
  });

  it("guard_nullOrWhitespace_validatesNonBlankStrings", () => {
    // Arrange & Act & Assert
    expect(Guard.against.nullOrWhitespace("  hello  ").ok).toBe(true);
    expect(Guard.against.nullOrWhitespace("   \t\n  ").ok).toBe(false);
    expect(Guard.against.nullOrWhitespace(null).ok).toBe(false);
  });

  it.each([
    [5, 1, 10, true],
    [1, 1, 10, true],
    [10, 1, 10, true],
    [0, 1, 10, false],
    [11, 1, 10, false],
  ])("guard_outOfRange_validatesInclusiveBounds(%i, %i, %i)", (val, min, max, expectedOk) => {
    // Act
    const res = Guard.against.outOfRange(val, min, max, "score");

    // Assert
    expect(res.ok).toBe(expectedOk);
  });

  it("guard_inequalities_validateBounds", () => {
    // Act & Assert
    expect(Guard.against.lessThan(5, 5).ok).toBe(true);
    expect(Guard.against.lessThan(4, 5).ok).toBe(false);

    expect(Guard.against.lessThanOrEqual(6, 5).ok).toBe(true);
    expect(Guard.against.lessThanOrEqual(5, 5).ok).toBe(false);

    expect(Guard.against.greaterThan(5, 5).ok).toBe(true);
    expect(Guard.against.greaterThan(6, 5).ok).toBe(false);

    expect(Guard.against.greaterThanOrEqual(4, 5).ok).toBe(true);
    expect(Guard.against.greaterThanOrEqual(5, 5).ok).toBe(false);
  });

  it("guard_lengthOutOfRange_validatesCharacterCount", () => {
    // Act & Assert
    expect(Guard.against.lengthOutOfRange("hello", 3, 10).ok).toBe(true);
    expect(Guard.against.lengthOutOfRange("hi", 3, 10).ok).toBe(false);
    expect(Guard.against.lengthOutOfRange("superlongstring", 3, 10).ok).toBe(false);
    expect(Guard.against.lengthOutOfRange("abc", 5, 2).ok).toBe(false);
  });

  it("guard_patternMismatch_validatesRegexMatch", () => {
    // Arrange
    const emailPattern = /^[^@]+@[^@]+\.[^@]+$/;

    // Act & Assert
    expect(Guard.against.patternMismatch("user@example.com", emailPattern).ok).toBe(true);
    expect(Guard.against.patternMismatch("invalid-email", emailPattern).ok).toBe(false);
    expect(Guard.against.patternMismatch(null, emailPattern).ok).toBe(false);
  });

  it("guard_trueAndFalse_validateBooleansAndPredicates", () => {
    // Act & Assert
    expect(Guard.against.true(false, "Condition was true").ok).toBe(true);
    expect(Guard.against.true(true, "Condition was true").ok).toBe(false);

    expect(Guard.against.false(true, "Condition was false").ok).toBe(true);
    expect(Guard.against.false(false, "Condition was false").ok).toBe(false);

    expect(Guard.against.true(10, (x) => x < 0, "Number cannot be negative").ok).toBe(true);
    expect(Guard.against.true(-1, (x) => x < 0, "Number cannot be negative").ok).toBe(false);

    expect(Guard.against.false(10, (x) => x > 0, "Number must be positive").ok).toBe(true);
    expect(Guard.against.false(-1, (x) => x > 0, "Number must be positive").ok).toBe(false);
  });

  it("guard_containsAndNotContains_validateCollectionPredicates", () => {
    // Arrange
    const items = [1, 2, 3, 4];

    // Act & Assert
    expect(Guard.against.contains(items, (x) => x > 10, "Contains element > 10").ok).toBe(true);
    expect(Guard.against.contains(items, (x) => x === 3, "Contains 3").ok).toBe(false);

    expect(Guard.against.notContains(items, (x) => x === 2, "Does not contain 2").ok).toBe(true);
    expect(Guard.against.notContains(items, (x) => x === 99, "Does not contain 99").ok).toBe(false);
  });
});
