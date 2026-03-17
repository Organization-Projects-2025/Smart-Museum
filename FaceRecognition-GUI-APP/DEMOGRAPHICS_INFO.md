# Face Demographics Collection - Information Guide

## 📊 What Information Is Collected?

### Saved to Database (Permanent):
1. **Age** - Estimated age at registration (e.g., 25, 30, 45)
2. **Gender** - Male/Female at registration
3. **Registration Date** - When first registered
4. **Visit Count** - Number of visits
5. **Last Visit** - Most recent visit timestamp

### Detected Per Visit (Not Saved):
1. **Emotion** - Current mood (Happy, Sad, Angry, Neutral, Fear, Disgust, Surprise)
   - Detected fresh on each visit
   - Shown on welcome screen
   - Not stored in database

### NOT Collected:
- ❌ Race/Ethnicity (removed for privacy)
- ❌ Historical emotions (only current)

## 🗄️ Data Storage Structure

### user_database.json
```json
{
  "user0": {
    "registered_date": "2026-03-17 14:30:00",
    "age": 28,
    "gender": "Man",
    "visit_count": 3,
    "last_visit": "2026-03-17 16:45:00"
  },
  "Ahmed": {
    "registered_date": "2026-03-15 10:00:00",
    "age": 35,
    "gender": "Man",
    "visit_count": 5,
    "last_visit": "2026-03-17 14:20:00"
  }
}
```

**Note:** Emotion is detected but NOT saved - only displayed during current visit.

## 🚀 How to Use

### Run the Enhanced Version:
```bash
python main_with_demographics.py
```

### What Happens:

1. **Camera Opens** - Face detection starts
2. **Position Face** - Follow on-screen guidance
3. **Recognition Attempt** - System tries to recognize you
4. **If Known User:**
   - Instant recognition
   - Demographics analyzed for display
   - Visit count updated
   - Welcome screen shows profile
5. **If New User:**
   - 3-second countdown
   - Demographics analyzed
   - Photo captured
   - Data saved to database
   - Welcome screen shows profile

## 📈 Use Cases for Demographics

### 1. Museum Analytics
- **Age Distribution**: Which age groups visit most?
- **Gender Balance**: Male/Female visitor ratio
- **Emotion Tracking**: Are visitors happy/engaged?
- **Peak Times**: When do different demographics visit?

### 2. Personalized Experience
- **Age-Appropriate Content**: Show relevant exhibits
- **Language Selection**: Based on ethnicity/race
- **Accessibility**: Adjust for elderly visitors
- **Marketing**: Target specific demographics

### 3. Security & Safety
- **Child Detection**: Alert if unaccompanied
- **Emotion Monitoring**: Detect distress
- **Crowd Analysis**: Age/gender distribution
- **VIP Recognition**: Special treatment for members

## 🔒 Privacy & Ethics

### Important Considerations:

1. **Consent**: Inform visitors about data collection
2. **Storage**: Secure database, encrypted if needed
3. **Retention**: Delete data after X days/months
4. **Anonymization**: Don't link to personal identity
5. **Opt-Out**: Allow visitors to decline
6. **Compliance**: Follow GDPR/local privacy laws

### Recommended Practices:

```python
# Add consent screen before face recognition
# Store only aggregated statistics, not individual data
# Implement data retention policy (e.g., 30 days)
# Allow users to request data deletion
# Don't share data with third parties
```

## 📊 Analytics Dashboard (Future Enhancement)

### Possible Metrics:

```
Daily Visitors: 245
Age Distribution:
  0-18:  15% (37 visitors)
  19-35: 45% (110 visitors)
  36-50: 25% (61 visitors)
  51+:   15% (37 visitors)

Gender Distribution:
  Male:   52% (127 visitors)
  Female: 48% (118 visitors)

Emotion Distribution:
  Happy:   60%
  Neutral: 30%
  Sad:     5%
  Other:   5%

Peak Hours:
  10-12 AM: 80 visitors
  2-4 PM:   95 visitors
  6-8 PM:   70 visitors
```

## 🛠️ Technical Details

### DeepFace Models Used:

1. **Age Model**: VGG-Face based
2. **Gender Model**: VGG-Face based
3. **Emotion Model**: Mini-XCEPTION
4. **Race Model**: VGG-Face based

### Processing Time:
- Face Detection: ~50ms
- Demographics Analysis: ~500-1000ms
- Total: ~1 second per face

### Accuracy:
- Age: ±5 years (85% within range)
- Gender: 95%+ accuracy
- Emotion: 85%+ accuracy
- Race: 80%+ accuracy

## 📝 Comparison: main.py vs main_with_demographics.py

| Feature | main.py | main_with_demographics.py |
|---------|---------|---------------------------|
| Face Recognition | ✅ | ✅ |
| Auto Sign-In/Up | ✅ | ✅ |
| Age Detection | ❌ | ✅ |
| Gender Detection | ❌ | ✅ |
| Emotion Detection | ❌ | ✅ |
| Race Detection | ❌ | ✅ |
| User Database | ❌ | ✅ (JSON file) |
| Visit Tracking | ❌ | ✅ |
| Analytics Ready | ❌ | ✅ |
| Processing Time | Fast | Slightly slower (+1s) |

## 🎯 Recommendations

### For Grand Egyptian Museum:

1. **Use Demographics** for:
   - Visitor analytics
   - Personalized recommendations
   - Marketing insights
   - Crowd management

2. **Don't Use** for:
   - Discrimination
   - Profiling
   - Selling data
   - Invasive tracking

3. **Best Practices**:
   - Display privacy notice
   - Offer opt-out option
   - Aggregate data only
   - Regular data cleanup
   - Secure storage

## 🔄 Migration Path

### From main.py to main_with_demographics.py:

1. **Backup** existing people/ folder
2. **Run** main_with_demographics.py
3. **Existing users** will be analyzed on next visit
4. **New users** get full demographics
5. **Database** created automatically

### No Breaking Changes:
- All existing face images work
- Same recognition accuracy
- Just adds extra features

## 📚 Additional Resources

- DeepFace Documentation: https://github.com/serengil/deepface
- Face Recognition Ethics: https://www.eff.org/issues/face-recognition
- GDPR Compliance: https://gdpr.eu/

## ⚠️ Legal Disclaimer

This system collects biometric and demographic data. Ensure compliance with:
- Local privacy laws
- GDPR (if in EU)
- CCPA (if in California)
- Museum policies
- Visitor consent requirements

Always consult legal counsel before deploying in production.
