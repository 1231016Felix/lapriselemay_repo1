using System.Diagnostics.CodeAnalysis;

namespace Shared.Core;

/// <summary>
/// Représente le résultat d'une opération qui peut réussir ou échouer.
/// Pattern Result/Either pour une gestion d'erreur explicite sans exceptions.
/// </summary>
/// <typeparam name="T">Type de la valeur en cas de succès</typeparam>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly string? _error;
    private readonly Exception? _exception;
    
    /// <summary>
    /// Indique si l'opération a réussi
    /// </summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }
    
    /// <summary>
    /// Indique si l'opération a échoué
    /// </summary>
    public bool IsFailure => !IsSuccess;
    
    /// <summary>
    /// Valeur en cas de succès (null si échec)
    /// </summary>
    public T? Value => _value;
    
    /// <summary>
    /// Message d'erreur en cas d'échec (null si succès)
    /// </summary>
    public string? Error => _error;
    
    /// <summary>
    /// Exception associée à l'échec (optionnel)
    /// </summary>
    public Exception? Exception => _exception;

    private Result(T value)
    {
        IsSuccess = true;
        _value = value;
        _error = null;
        _exception = null;
    }

    private Result(string error, Exception? exception = null)
    {
        IsSuccess = false;
        _value = default;
        _error = error;
        _exception = exception;
    }

    /// <summary>
    /// Crée un résultat de succès
    /// </summary>
    public static Result<T> Success(T value) => new(value);
    
    /// <summary>
    /// Crée un résultat d'échec
    /// </summary>
    public static Result<T> Failure(string error, Exception? exception = null) => new(error, exception);

    /// <summary>
    /// Conversion implicite depuis une valeur
    /// </summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>
    /// Obtient la valeur ou une valeur par défaut
    /// </summary>
    public T GetValueOrDefault(T defaultValue = default!) => IsSuccess ? _value! : defaultValue;
    
    /// <summary>
    /// Obtient la valeur ou lance une exception
    /// </summary>
    /// <exception cref="InvalidOperationException">Si le résultat est un échec</exception>
    public T GetValueOrThrow()
    {
        if (IsFailure)
        {
            throw _exception ?? new InvalidOperationException(_error);
        }
        return _value!;
    }

    /// <summary>
    /// Exécute une action si le résultat est un succès
    /// </summary>
    public Result<T> OnSuccess(Action<T> action)
    {
        if (IsSuccess) action(_value!);
        return this;
    }
    
    /// <summary>
    /// Exécute une action si le résultat est un échec
    /// </summary>
    public Result<T> OnFailure(Action<string> action)
    {
        if (IsFailure) action(_error!);
        return this;
    }

    /// <summary>
    /// Transforme la valeur si succès
    /// </summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        return IsSuccess 
            ? Result<TNew>.Success(mapper(_value!)) 
            : Result<TNew>.Failure(_error!, _exception);
    }
    
    /// <summary>
    /// Transforme la valeur si succès (avec Result)
    /// </summary>
    public Result<TNew> Bind<TNew>(Func<T, Result<TNew>> mapper)
    {
        return IsSuccess ? mapper(_value!) : Result<TNew>.Failure(_error!, _exception);
    }

    /// <summary>
    /// Pattern matching
    /// </summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<string, TResult> onFailure)
    {
        return IsSuccess ? onSuccess(_value!) : onFailure(_error!);
    }

    public override string ToString()
    {
        return IsSuccess ? $"Success({_value})" : $"Failure({_error})";
    }
}

/// <summary>
/// Représente le résultat d'une opération sans valeur de retour.
/// </summary>
public readonly struct Result
{
    private readonly string? _error;
    private readonly Exception? _exception;

    /// <summary>
    /// Indique si l'opération a réussi
    /// </summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }
    
    /// <summary>
    /// Indique si l'opération a échoué
    /// </summary>
    public bool IsFailure => !IsSuccess;
    
    /// <summary>
    /// Message d'erreur en cas d'échec
    /// </summary>
    public string? Error => _error;
    
    /// <summary>
    /// Exception associée à l'échec
    /// </summary>
    public Exception? Exception => _exception;

    private Result(bool success, string? error = null, Exception? exception = null)
    {
        IsSuccess = success;
        _error = error;
        _exception = exception;
    }

    /// <summary>
    /// Résultat de succès singleton
    /// </summary>
    public static Result Success() => new(true);
    
    /// <summary>
    /// Crée un résultat d'échec
    /// </summary>
    public static Result Failure(string error, Exception? exception = null) => new(false, error, exception);
    
    /// <summary>
    /// Crée un Result&lt;T&gt; de succès
    /// </summary>
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    
    /// <summary>
    /// Crée un Result&lt;T&gt; d'échec
    /// </summary>
    public static Result<T> Failure<T>(string error, Exception? exception = null) => Result<T>.Failure(error, exception);

    /// <summary>
    /// Exécute une action de manière sécurisée et retourne un Result
    /// </summary>
    public static Result Try(Action action)
    {
        try
        {
            action();
            return Success();
        }
        catch (Exception ex)
        {
            return Failure(ex.Message, ex);
        }
    }
    
    /// <summary>
    /// Exécute une fonction de manière sécurisée et retourne un Result&lt;T&gt;
    /// </summary>
    public static Result<T> Try<T>(Func<T> func)
    {
        try
        {
            return Result<T>.Success(func());
        }
        catch (Exception ex)
        {
            return Result<T>.Failure(ex.Message, ex);
        }
    }
    
    /// <summary>
    /// Exécute une action async de manière sécurisée
    /// </summary>
    public static async Task<Result> TryAsync(Func<Task> action)
    {
        try
        {
            await action();
            return Success();
        }
        catch (Exception ex)
        {
            return Failure(ex.Message, ex);
        }
    }
    
    /// <summary>
    /// Exécute une fonction async de manière sécurisée
    /// </summary>
    public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> func)
    {
        try
        {
            return Result<T>.Success(await func());
        }
        catch (Exception ex)
        {
            return Result<T>.Failure(ex.Message, ex);
        }
    }

    /// <summary>
    /// Exécute une action si succès
    /// </summary>
    public Result OnSuccess(Action action)
    {
        if (IsSuccess) action();
        return this;
    }
    
    /// <summary>
    /// Exécute une action si échec
    /// </summary>
    public Result OnFailure(Action<string> action)
    {
        if (IsFailure) action(_error!);
        return this;
    }

    public override string ToString()
    {
        return IsSuccess ? "Success" : $"Failure({_error})";
    }
}

/// <summary>
/// Extensions pour faciliter l'utilisation de Result
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Combine plusieurs résultats - tous doivent réussir
    /// </summary>
    public static Result Combine(params Result[] results)
    {
        foreach (var result in results)
        {
            if (result.IsFailure)
                return result;
        }
        return Result.Success();
    }
    
    /// <summary>
    /// Convertit une valeur nullable en Result
    /// </summary>
    public static Result<T> ToResult<T>(this T? value, string errorIfNull = "Value is null") where T : class
    {
        return value is not null 
            ? Result<T>.Success(value) 
            : Result<T>.Failure(errorIfNull);
    }
    
    /// <summary>
    /// Convertit une valeur nullable struct en Result
    /// </summary>
    public static Result<T> ToResult<T>(this T? value, string errorIfNull = "Value is null") where T : struct
    {
        return value.HasValue 
            ? Result<T>.Success(value.Value) 
            : Result<T>.Failure(errorIfNull);
    }
}
