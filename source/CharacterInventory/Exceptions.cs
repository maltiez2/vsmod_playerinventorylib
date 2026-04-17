namespace PlayerInventoryLib;

public class DuplicatedSlotIdException : Exception
{
    public DuplicatedSlotIdException()
    {
    }

    public DuplicatedSlotIdException(string message)
        : base("[Player Inventory lib] " + message)
    {
    }

    public DuplicatedSlotIdException(string message, Exception innerException)
        : base("[Player Inventory lib] " + message, innerException)
    {
    }
}

public class UnknownSlotIdException : Exception
{
    public UnknownSlotIdException()
    {
    }

    public UnknownSlotIdException(string message)
        : base("[Player Inventory lib] " + message)
    {
    }

    public UnknownSlotIdException(string message, Exception innerException)
        : base("[Player Inventory lib] " + message, innerException)
    {
    }
}