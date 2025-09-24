namespace SundouleiaShared.RequirementHandlers;

public enum UserRequirements
{
    Identified = 0b00000001, // Identified valid user.
    TempAccess = 0b00000010, // Only allow temporary access.
    Admin      = 0b00000100, // Admin of Sundouleia
}
