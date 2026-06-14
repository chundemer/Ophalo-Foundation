namespace OpHalo.Foundation.Application.Accounts.Access;

public interface IAccountAccessPolicy
{
    AccountAccessDecision Evaluate(AccountAccessContext context);
}
