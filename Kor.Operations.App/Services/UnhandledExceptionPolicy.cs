#nullable enable
using System;
using System.Linq;

namespace Kor.Operations.App.Services;

internal static class UnhandledExceptionPolicy
{
    internal const string FatalMessage =
        "KOR hit a serious system error and must close. Restart the app; if it happens again, contact IT support.";

    internal static UnhandledExceptionDecision Decide(Exception exception)
    {
        if (ContainsFatalException(exception))
        {
            return new UnhandledExceptionDecision(FatalMessage, CanContinue: false);
        }

        return new UnhandledExceptionDecision(UserFacingExceptionMapper.Map(exception), CanContinue: true);
    }

    private static bool ContainsFatalException(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            return aggregate.Flatten().InnerExceptions.Any(ContainsFatalException);
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OutOfMemoryException
                or StackOverflowException
                or AccessViolationException
                or AppDomainUnloadedException
                or BadImageFormatException)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record UnhandledExceptionDecision(string UserMessage, bool CanContinue);
