﻿namespace KaizokuBackend.Models.Database;

public enum ImportStatus
{
    Import = 0,
    Skip = 1,
    DoNotChange = 2,
    Completed = 3
}