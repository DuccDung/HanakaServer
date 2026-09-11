using System.Reflection;
using Microsoft.Data.SqlClient;

namespace HanakaServer.Tests;

// SqlException has no public constructor. Recreate captured provider errors without
// deliberately failing requests against a real database or relying on message-only mocks.
internal static class SqlExceptionTestFactory
{
    public static SqlException Create(params (int Number, string Message)[] errors)
    {
        var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        var constructor = typeof(SqlError).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .OrderBy(x => x.GetParameters().Length).First();
        var add = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var (number, message) in errors)
        {
            var args = constructor.GetParameters().Select(p => p.Name switch
            {
                "infoNumber" => (object)number,
                "errorMessage" => message,
                "errorState" => number == 0 ? (byte)0 : (byte)1,
                "errorClass" => number == 0 ? (byte)11 : (byte)16,
                "server" => "isolated-test",
                "procedure" => "",
                "lineNumber" => 1,
                _ => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null
            }).ToArray();
            add.Invoke(collection, [constructor.Invoke(args)]);
        }

        var create = typeof(SqlException).GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic,
            binder: null, types: [typeof(SqlErrorCollection), typeof(string)], modifiers: null)!;
        return (SqlException)create.Invoke(null, [collection, "15.0.2000.5"])!;
    }

    public static SqlException CancelledBatch() => Create(
        (3980, "The request failed to run because the batch is aborted, this can be caused by abort signal sent from client, or another request is running in the same session, which makes the session busy."),
        (0, "Operation cancelled by user."));
}
