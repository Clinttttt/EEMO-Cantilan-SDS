# Facility Reports Modal - Facility-Specific Data Context

## Overview

The **FacilityReportsModal** now displays **facility-specific mock data** based on the `FacilityCode` parameter. Each facility shows different revenue patterns, stall counts, collection rates, and top performers that reflect their unique business context.

## Facility-Specific Mock Data

### 1. NPM (New Public Market)
**Business Model**: Daily collection (₱30/day + utilities + fish fees)

| Metric | Value | Context |
|--------|-------|---------|
| **Total Revenue** | ₱125,450.00 | Highest volume - daily collections |
| **Revenue Growth** | +12.5% | Strong growth from daily operations |
| **Collection Rate** | 87% | Good daily compliance |
| **Total Stalls** | 32 | Medium-sized market |
| **Occupied** | 28 stalls | 87.5% occupancy |
| **Pending Payments** | 4 stalls (₱18,200) | Some daily arrears |

**Section Breakdown**:
- Vegetable Area: ₱45,200 (36%)
- Fish Section: ₱52,150 (42%) - Highest due to fish fees
- Meat Section: ₱28,100 (22%)

**Top Stalls**:
1. F-12 - Rosa Gutierrez (₱5,200)
2. V-08 - Adan Bohemian (₱4,850)
3. M-05 - Juan Santos (₱4,200)
4. F-03 - Maria Cruz (₱3,950)

**Revenue Trend** (Monthly): ₱95k → ₱102k → ₱98.5k → ₱105k → ₱112k → ₱125.45k

---

### 2. TCC (Tampak Commercial Center)
**Business Model**: Monthly rental (₱2,400-₱4,800)

| Metric | Value | Context |
|--------|-------|---------|
| **Total Revenue** | ₱89,600.00 | Stable monthly rental income |
| **Revenue Growth** | +8.3% | Steady commercial growth |
| **Collection Rate** | 92% | Excellent compliance |
| **Total Stalls** | 25 | Smaller, premium facility |
| **Occupied** | 23 stalls | 92% occupancy |
| **Pending Payments** | 2 stalls (₱7,200) | Minimal arrears |

**Top Stalls**:
1. TCC-01 - Liza's Boutique (₱4,800)
2. TCC-15 - Tech Haven Store (₱4,800)
3. TCC-08 - Mang Tomas Eatery (₱3,600)
4. TCC-22 - Beauty Corner (₱3,600)

**Revenue Trend** (Monthly): ₱72k → ₱76.8k → ₱81.6k → ₱84k → ₱86.4k → ₱89.6k

---

### 3. NCC (New Commercial Center)
**Business Model**: Monthly rental (₱1,200-₱3,840)

| Metric | Value | Context |
|--------|-------|---------|
| **Total Revenue** | ₱67,200.00 | Growing commercial area |
| **Revenue Growth** | +15.7% | Highest growth rate |
| **Collection Rate** | 78% | Lower compliance, needs attention |
| **Total Stalls** | 45 | Largest facility |
| **Occupied** | 35 stalls | 77.8% occupancy |
| **Pending Payments** | 7 stalls (₱12,800) | Higher arrears |

**Section Breakdown**:
- Corner Stalls: ₱38,400 (57%) - Premium locations
- Extension Area: ₱28,800 (43%)

**Top Stalls**:
1. NCC-C12 - Golden Harvest Store (₱3,840)
2. NCC-C05 - Sunrise Bakery (₱3,200)
3. NCC-E18 - Fresh Mart (₱2,400)
4. NCC-C08 - Variety Shop (₱2,400)

**Revenue Trend** (Monthly): ₱48k → ₱52.8k → ₱57.6k → ₱60k → ₱64.8k → ₱67.2k

---

### 4. BBQ (BBQ Stand)
**Business Model**: Monthly space rental (₱1,600-₱3,200)

| Metric | Value | Context |
|--------|-------|---------|
| **Total Revenue** | ₱38,400.00 | Seasonal business |
| **Revenue Growth** | +6.2% | Moderate growth |
| **Collection Rate** | 85% | Good compliance |
| **Total Stalls** | 15 | Small specialized facility |
| **Occupied** | 12 stalls | 80% occupancy |
| **Pending Payments** | 3 stalls (₱6,400) | Some seasonal delays |

**Top Stalls**:
1. BBQ-01 - Mang Inasal BBQ (₱3,200)
2. BBQ-05 - Grill Master (₱3,200)
3. BBQ-03 - Smokey's BBQ (₱2,400)
4. BBQ-08 - BBQ King (₱1,600)

**Revenue Trend** (Monthly): ₱28.8k → ₱32k → ₱33.6k → ₱35.2k → ₱36.8k → ₱38.4k

---

### 5. ICE (Iceplant)
**Business Model**: Monthly space rental (₱1,000-₱2,000)

| Metric | Value | Context |
|--------|-------|---------|
| **Total Revenue** | ₱24,000.00 | Smallest facility |
| **Revenue Growth** | +4.8% | Stable, low growth |
| **Collection Rate** | 95% | Best compliance rate |
| **Total Stalls** | 10 | Very small facility |
| **Occupied** | 8 stalls | 80% occupancy |
| **Pending Payments** | 2 stalls (₱4,000) | Minimal issues |

**Top Stalls**:
1. ICE-01 - Frozen Delights (₱2,000)
2. ICE-03 - Ice Factory Co. (₱2,000)
3. ICE-02 - Cool Breeze Ice (₱1,500)
4. ICE-04 - Arctic Ice Supply (₱1,000)

**Revenue Trend** (Monthly): ₱18k → ₱19k → ₱20k → ₱21k → ₱22.5k → ₱24k

---

## Revenue Chart Data by Period

### Weekly Data (7 days)
- **NPM**: ₱15.2k → ₱18.5k → ₱17.8k → ₱19.2k → ₱16.5k → ₱20.1k → ₱18.15k
- **TCC**: ₱18.2k → ₱19.8k → ₱21.5k → ₱20.1k → ₱22.4k → ₱23.1k → ₱21.8k
- **NCC**: ₱12.8k → ₱14.2k → ₱13.5k → ₱15.8k → ₱16.2k → ₱17.1k → ₱15.6k
- **BBQ**: ₱7.2k → ₱8.4k → ₱9.6k → ₱8.8k → ₱10.2k → ₱9.8k → ₱9.2k
- **ICE**: ₱4.8k → ₱5.2k → ₱5.6k → ₱5.4k → ₱6.0k → ₱5.8k → ₱5.6k

### Monthly Data (6 months)
- **NPM**: ₱95k → ₱102k → ₱98.5k → ₱105k → ₱112k → ₱125.45k
- **TCC**: ₱72k → ₱76.8k → ₱81.6k → ₱84k → ₱86.4k → ₱89.6k
- **NCC**: ₱48k → ₱52.8k → ₱57.6k → ₱60k → ₱64.8k → ₱67.2k
- **BBQ**: ₱28.8k → ₱32k → ₱33.6k → ₱35.2k → ₱36.8k → ₱38.4k
- **ICE**: ₱18k → ₱19k → ₱20k → ₱21k → ₱22.5k → ₱24k

### Yearly Data (5 years)
- **NPM**: ₱980k → ₱1.05M → ₱1.125M → ₱1.245M → ₱1.35M
- **TCC**: ₱720k → ₱768k → ₱816k → ₱864k → ₱912k
- **NCC**: ₱480k → ₱528k → ₱576k → ₱624k → ₱672k
- **BBQ**: ₱288k → ₱320k → ₱336k → ₱352k → ₱384k
- **ICE**: ₱180k → ₱190k → ₱200k → ₱220k → ₱240k

---

## Key Insights by Facility

### NPM (New Public Market)
- **Highest revenue** due to daily collection model
- **Fish section dominates** (42%) due to ₱1/kg fish fees
- **Moderate collection rate** (87%) - daily compliance challenges
- **Strong growth** (+12.5%) - expanding vendor base

### TCC (Tampak Commercial Center)
- **Best collection rate** (92%) - reliable commercial tenants
- **Stable revenue** - monthly rental model
- **Premium rates** - ₱4,800 for prime stalls
- **Low arrears** - only 2 pending payments

### NCC (New Commercial Center)
- **Highest growth rate** (+15.7%) - emerging commercial hub
- **Largest facility** (45 stalls) - most expansion potential
- **Lowest collection rate** (78%) - needs collection improvement
- **Corner stalls premium** - 57% of revenue from corner locations

### BBQ (BBQ Stand)
- **Seasonal patterns** - revenue varies with events/seasons
- **Specialized niche** - BBQ vendors only
- **Moderate compliance** (85%) - typical for food vendors
- **Smaller scale** - 15 stalls, focused operation

### ICE (Iceplant)
- **Best collection rate** (95%) - most reliable tenants
- **Smallest facility** (10 stalls) - limited scale
- **Stable business** - low growth but consistent
- **Minimal issues** - only 2 pending payments

---

## Implementation Notes

### Data Structure
All mock data is defined using C# switch expressions based on `FacilityCode`:

```csharp
private decimal MockTotalRevenue => FacilityCode switch
{
    "NPM" => 125450.00m,
    "TCC" => 89600.00m,
    "NCC" => 67200.00m,
    "BBQ" => 38400.00m,
    "ICE" => 24000.00m,
    _ => 100000.00m
};
```

### Chart Data
Revenue trends use tuple pattern matching for facility + period:

```csharp
var chartData = (FacilityCode, SelectedPeriod) switch
{
    ("NPM", "Weekly") => new[] { 15200m, 18500m, ... },
    ("TCC", "Monthly") => new[] { 72000m, 76800m, ... },
    // ... etc
};
```

### Section Breakdown
Only NPM and NCC have section breakdowns:
- **NPM**: Vegetable Area, Fish Section, Meat Section
- **NCC**: Corner Stalls, Extension Area
- **TCC, BBQ, ICE**: No sections (ShowSectionBreakdown = false)

---

## Future API Integration

When replacing mock data with real API calls, maintain the same facility-specific context:

1. **Query by FacilityCode** - Each API call should filter by facility
2. **Respect business models** - NPM uses daily rates, others use monthly
3. **Section-aware queries** - NPM and NCC need section breakdowns
4. **Period-specific data** - Weekly/Monthly/Yearly aggregations
5. **Top performers** - Rank by actual revenue per facility

---

**Status**: ✅ **COMPLETE** - Each facility now displays its own unique data context
**Date**: 2025
**Mock Data**: Facility-specific, ready for API replacement
