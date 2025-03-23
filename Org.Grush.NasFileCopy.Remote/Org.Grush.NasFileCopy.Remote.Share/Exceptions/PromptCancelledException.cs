namespace Org.Grush.NasFileCopy.Remote.Share.Exceptions;

public class PromptCancelledException(string message = "Prompt cancelled") : Exception(message);