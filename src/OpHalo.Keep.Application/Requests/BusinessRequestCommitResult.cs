namespace OpHalo.Keep.Application.Requests;

public enum BusinessRequestCommitResult
{
    Committed = 1,
    UniqueTokenCollision = 2,
    CustomerCanonicalPhoneCollision = 3
}
