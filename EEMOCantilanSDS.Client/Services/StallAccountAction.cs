namespace EEMOCantilanSDS.Client.Services;

/// <summary>The operational actions a permanent stall's inactive account offers. Assign is placing the payor in a NEW stall; it is not a space handover.</summary>
public enum StallAccountAction { Resume = 1, Renew = 2, Assign = 3, Remove = 4 }
