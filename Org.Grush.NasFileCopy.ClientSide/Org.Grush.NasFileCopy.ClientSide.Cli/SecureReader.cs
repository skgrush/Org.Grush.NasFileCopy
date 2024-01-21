
namespace Org.Grush.NasFileCopy.ClientSide.Cli;

public static class SecureReader
{
  public static string Read()
  {
    var pwd = "";
    while (true)
    {
      var inp = Console.ReadKey(true);
      switch (inp.Key)
      {
        case ConsoleKey.Enter:
          return pwd;

        case ConsoleKey.Backspace:
          if (pwd.Length is not 0)
          {
            pwd = pwd[..^1];
            Console.Write("\b \b");
          }

          continue;
      }

      if (inp.KeyChar is '\u0000')
        continue;

      pwd += inp.KeyChar;
      Console.Write("*");
    }
  }
}