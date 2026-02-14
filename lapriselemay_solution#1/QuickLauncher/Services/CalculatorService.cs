using System.Globalization;
using System.Text.RegularExpressions;

namespace QuickLauncher.Services;

/// <summary>
/// Service de calcul mathématique avec parser récursif descendant.
/// Remplace DataTable.Compute qui ne supportait pas ^ (puissance),
/// les fonctions math (sqrt, sin, cos, etc.), ni les constantes (pi, e).
/// 
/// Amélioration #7 : support de :
///   - Opérateurs : + - * / ^ %
///   - Parenthèses : ( )
///   - Fonctions : sqrt, sin, cos, tan, log, ln, abs, ceil, floor, round
///   - Constantes : pi, e
///   - Nombres décimaux avec . ou ,
/// 
/// Grammaire (précédence croissante) :
///   expr   → term (('+' | '-') term)*
///   term   → power (('*' | '/' | '%') power)*
///   power  → unary ('^' unary)*     (associativité droite)
///   unary  → ('-' | '+') unary | call
///   call   → IDENT '(' expr ')' | primary
///   primary→ NUMBER | '(' expr ')'
/// </summary>
public static partial class CalculatorService
{
    [GeneratedRegex(@"^[\d\s\+\-\*\/\(\)\.\,\^%a-z]+$", RegexOptions.IgnoreCase)]
    private static partial Regex MathExpressionRegex();

    /// <summary>
    /// Tente d'évaluer une expression mathématique.
    /// </summary>
    public static bool TryCalculate(string expression, out string result)
    {
        result = string.Empty;

        if (string.IsNullOrWhiteSpace(expression))
            return false;

        // Vérification rapide : doit contenir au moins un opérateur ou une fonction
        if (!MathExpressionRegex().IsMatch(expression))
            return false;

        // Doit contenir un opérateur ou une fonction pour être une expression
        var hasOperator = expression.Any(c => "+-*/^%".Contains(c));
        var hasFunction = KnownFunctions.Any(f => expression.Contains(f, StringComparison.OrdinalIgnoreCase));
        if (!hasOperator && !hasFunction)
            return false;

        try
        {
            var normalized = expression.Replace(',', '.').Replace(" ", "");
            var parser = new MathParser(normalized);
            var value = parser.ParseExpression();

            // Vérifier qu'on a consommé toute l'entrée
            if (parser.Position < parser.Input.Length)
                return false;

            if (double.IsNaN(value) || double.IsInfinity(value))
                return false;

            result = FormatResult(value);
            return !string.IsNullOrEmpty(result);
        }
        catch
        {
            return false;
        }
    }

    private static string FormatResult(double value)
    {
        // Entier exact
        if (Math.Abs(value % 1) < 1e-10 && Math.Abs(value) < 1e15)
            return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);

        return value.ToString("G10", CultureInfo.InvariantCulture);
    }

    private static readonly string[] KnownFunctions =
        ["sqrt", "sin", "cos", "tan", "log", "ln", "abs", "ceil", "floor", "round"];

    /// <summary>
    /// Parser récursif descendant pour expressions mathématiques.
    /// </summary>
    private ref struct MathParser
    {
        public readonly string Input;
        public int Position;

        public MathParser(string input)
        {
            Input = input;
            Position = 0;
        }

        public double ParseExpression()
        {
            var left = ParseTerm();

            while (Position < Input.Length)
            {
                var ch = Input[Position];
                if (ch == '+') { Position++; left += ParseTerm(); }
                else if (ch == '-') { Position++; left -= ParseTerm(); }
                else break;
            }

            return left;
        }

        private double ParseTerm()
        {
            var left = ParsePower();

            while (Position < Input.Length)
            {
                var ch = Input[Position];
                if (ch == '*') { Position++; left *= ParsePower(); }
                else if (ch == '/') { Position++; left /= ParsePower(); }
                else if (ch == '%') { Position++; left %= ParsePower(); }
                else break;
            }

            return left;
        }

        private double ParsePower()
        {
            var baseVal = ParseUnary();

            if (Position < Input.Length && Input[Position] == '^')
            {
                Position++;
                // Associativité droite : 2^3^2 = 2^(3^2) = 512
                var exp = ParsePower();
                return Math.Pow(baseVal, exp);
            }

            return baseVal;
        }

        private double ParseUnary()
        {
            if (Position < Input.Length)
            {
                if (Input[Position] == '-') { Position++; return -ParseUnary(); }
                if (Input[Position] == '+') { Position++; return ParseUnary(); }
            }

            return ParseCall();
        }

        private double ParseCall()
        {
            // Essayer de lire un identifiant (fonction ou constante)
            if (Position < Input.Length && char.IsLetter(Input[Position]))
            {
                var start = Position;
                while (Position < Input.Length && char.IsLetter(Input[Position]))
                    Position++;

                var name = Input[start..Position].ToLowerInvariant();

                // Constantes
                if (name == "pi") return Math.PI;
                if (name == "e" && (Position >= Input.Length || Input[Position] != '('))
                    return Math.E;

                // Fonctions : doit être suivi de '('
                if (Position < Input.Length && Input[Position] == '(')
                {
                    Position++; // skip '('
                    var arg = ParseExpression();

                    if (Position < Input.Length && Input[Position] == ')')
                        Position++; // skip ')'

                    return name switch
                    {
                        "sqrt" => Math.Sqrt(arg),
                        "sin" => Math.Sin(arg),
                        "cos" => Math.Cos(arg),
                        "tan" => Math.Tan(arg),
                        "log" => Math.Log10(arg),
                        "ln" => Math.Log(arg),
                        "abs" => Math.Abs(arg),
                        "ceil" => Math.Ceiling(arg),
                        "floor" => Math.Floor(arg),
                        "round" => Math.Round(arg),
                        "exp" => Math.Exp(arg),
                        _ => throw new FormatException($"Unknown function: {name}")
                    };
                }

                throw new FormatException($"Unknown identifier: {name}");
            }

            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            // Parenthèses
            if (Position < Input.Length && Input[Position] == '(')
            {
                Position++; // skip '('
                var value = ParseExpression();

                if (Position < Input.Length && Input[Position] == ')')
                    Position++; // skip ')'

                return value;
            }

            // Nombre
            return ParseNumber();
        }

        private double ParseNumber()
        {
            var start = Position;

            while (Position < Input.Length && (char.IsDigit(Input[Position]) || Input[Position] == '.'))
                Position++;

            if (Position == start)
                throw new FormatException($"Expected number at position {Position}");

            var span = Input.AsSpan(start, Position - start);
            return double.Parse(span, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
