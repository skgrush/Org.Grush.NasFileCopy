using System.Collections.Immutable;

namespace Org.Grush.NasFileCopy.Remote.Share.Validation;

public static class PathValidation
{
  public static readonly ImmutableList<(string invalidSequence, string? label)> InvalidSequences =
  [
    ("\\", "escapes (backslashes)"),
    ("..", null),
    ("\n", "newlines"),
    ("\r", "newlines"),
    ("\t", "tabs"),
  ];

  public static void ReplaceHomeTilde(ref string path)
  {
    if (path.StartsWith('~'))
      path  = "$HOME" + path[1..];
  }

  public static bool IsInvalidatePathEntry(
    string path,
    out List<string> errors,
    bool doubleQuotable = false,
    bool singleQuotable = false,
    bool notEmpty = false,
    bool noVariables = false,
    bool notAbsolute = false
  )
  {
    errors = ValidatePathEntry(
      path: path,
      doubleQuotable: doubleQuotable,
      singleQuotable: singleQuotable,
      notEmpty: notEmpty,
      noVariables: noVariables,
      notAbsolute: notAbsolute
    ).ToList();

    return errors.Count is not 0;
  }

  public static IEnumerable<string> ValidatePathEntry(
    string path,
    bool doubleQuotable = false,
    bool singleQuotable = false,
    bool notEmpty = false,
    bool noVariables = false,
    bool notAbsolute = false
  )
  {
    if (path is "")
    {
      if (notEmpty)
        yield return "cannot be empty";
      yield break; // all other validators pass on empty
    }

    foreach (var (invalidSequence, label) in InvalidSequences)
    {
      if (path.Contains(invalidSequence))
        yield return $"cannot contain {label ?? invalidSequence}";
    }
    if (path.StartsWith('~'))
      yield return "cannot start with '~'";

    if (doubleQuotable && path.Contains('"'))
      yield return $"cannot contain double-quotes";
    if (singleQuotable && path.Contains('\''))
      yield return $"cannot contain single-quotes";
    if (noVariables && path.Contains('$'))
      yield return "cannot contain variables ($)";
    if (notAbsolute && path[0] is '/')
      yield return "cannot start with slash";
  }
}