using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using UnityEngine;

public interface ITimedEvent {
    float TimeSinceEvent { get; }
}

public struct TimedEvent : ITimedEvent {
    internal const float GutterT = 10f;
    public const float MinTime = -9f;

    private float eventTime;
    public bool IsSet { get { return eventTime > 0; } }
    public float EventTime { get { return (IsSet ? eventTime : -eventTime) - GutterT; } }
    public float TimeSinceEvent { get { var t = EventTime; return t < 0.0f ? float.MaxValue : Clock - t; } }
    public TimedEvent(float time) { eventTime = time + GutterT; }

    public float TimeSinceState(bool state) {
        if (state == IsSet) return TimeSinceEvent;
        return -1;
    }
    public bool OccuredAfter(TimedEvent timed) { return eventTime > timed.eventTime; }

    public bool SetChecked(bool isSet = true) {
        if (IsSet == isSet) return false;
        Set(isSet);
        return true;
    }
    public void Set(bool isSet = true) {
        SetEventTime(Clock, isSet);
    }
    public void SetNoNotify(bool isSet = true) {
        SetEventTime(EventTime, isSet);
    }
    public void Clear() {
        if (IsSet) SetEventTime(Clock, false);
    }
    public void SetEventTime(float time) { SetEventTime(time, IsSet); }
    public void SetEventTime(float time, bool triggered) {
        Debug.Assert(time > -TimedEvent.GutterT, "Time will invert state!");
        eventTime = triggered ? time + GutterT : -(time + GutterT);
    }

    public bool FramePassesTime(float time) {
        var tdelta = TimeSinceEvent - time;
        return tdelta > 0 && tdelta <= ClockDelta;
    }
    public bool FramePassesTime(float first, float interval) {
        var tdelta = TimeSinceEvent - first;
        if (tdelta > interval) tdelta = (tdelta % interval);
        return tdelta > 0 && tdelta <= ClockDelta;
    }
    public bool FramePassesInterval(float interval, float delay = 0f) {
        if (TimeSinceEvent < delay) return false;
        var tdelta = (TimeSinceEvent - delay) % interval;
        return tdelta > 0 && tdelta <= TimedEvent.ClockDelta;
    }
    public int GetFrameTicks(float rate, float delay = 0f) {
        return GetFrameTicks(TimeSinceEvent, rate, delay);
    }
    public override string ToString() {
        return IsSet + ": Elapsed: " + TimeSinceEvent;
    }

    public static implicit operator TimedEvent(bool v) { var e = new TimedEvent(); e.Set(v); return e; }
    public static implicit operator bool(TimedEvent t) { return t.IsSet; }

    public static int GetFrameTicks(float time, float rate, float delay = 0f) {
        var time1 = time - delay;
        if (time1 < 0f) return 0;
        var time0 = time1 - ClockDelta;
        time0 *= rate;
        time1 *= rate;
        if (time0 < 0f) time0 = -1f;
        return (int)((uint)(time1) - (uint)(time0));
    }
    public static bool GetFramePassesTime(float time, float targetTime) {
        var tdelta = time - targetTime;
        return tdelta > 0 && tdelta <= TimedEvent.ClockDelta;
    }

    public static readonly TimedEvent DefaultOn = new TimedEvent() { eventTime = 0.0001f, };
    public static readonly TimedEvent DefaultOff = new TimedEvent() { eventTime = 0f, };
    public static float Clock { get { return Time.time; } }
    public static float ClockDelta { get { return Time.deltaTime; } }
    public static TimedEvent Now { get { return new TimedEvent(Clock); } }
}
public struct TimedEvent<T> : ITimedEvent {
    public T Value { get; private set; }

    private float eventTime;
    public bool IsSet { get { return eventTime > 0f; } }
    public float EventTime { get { return (IsSet ? eventTime : -eventTime) - TimedEvent.GutterT; } }
    public float TimeSinceEvent { get { return TimedEvent.Clock - EventTime; } }

    public TimedEvent(T value) {
        Value = value;
        eventTime = TimedEvent.Clock + TimedEvent.GutterT;
    }
    public TimedEvent(T value, float time) { Value = value; eventTime = time; }

    public void Set(T value, bool isSet = true) {
        Value = value;
        SetEventTime(TimedEvent.Clock, isSet);
    }
    public void SetNoNotify(T value) {
        Value = value;
    }
    public void Clear() {
        Value = default;
        if (IsSet) SetEventTime(TimedEvent.Clock, false);
    }
    public void SetEventTime(float time) { SetEventTime(time, IsSet); }
    public void SetEventTime(float time, bool isSet) {
        Debug.Assert(time > -TimedEvent.GutterT, "Time will invert state!");
        eventTime = isSet ? time + TimedEvent.GutterT : -(time + TimedEvent.GutterT);
    }

    public bool FramePassesTime(float time) {
        var tdelta = TimeSinceEvent - time;
        return tdelta > 0 && tdelta <= TimedEvent.ClockDelta;
    }
    public bool FramePassesInterval(float interval) {
        var tdelta = (TimeSinceEvent % interval);
        return tdelta > 0 && tdelta <= TimedEvent.ClockDelta;
    }
    public int GetFrameTicks(float interval, float delay = 0f) {
        var time1 = TimeSinceEvent - delay;
        if (time1 <= 0f) return 0;
        var time0 = time1 - TimedEvent.ClockDelta;
        if (time0 <= 0f) time0 = 0f;
        return (int)((uint)(time1 / interval) - (uint)(time0 / interval));
    }

    public T this[int i] { get { Debug.Assert(i == 0); return Value; } }

    public static implicit operator TimedEvent<T>(T t) { return new TimedEvent<T>(t); }
    public static implicit operator T(TimedEvent<T> t) { return t.Value; }

    public static bool operator ==(TimedEvent<T> e, T value) { return Compare(e.Value, value); }
    public static bool operator !=(TimedEvent<T> e, T value) { return !Compare(e.Value, value); }
    public static bool operator ==(T value, TimedEvent<T> e) { return Compare(e.Value, value); }
    public static bool operator !=(T value, TimedEvent<T> e) { return !Compare(e.Value, value); }

    private static bool Compare<S>(S item1, S item2) {
        if (typeof(S).IsValueType) return EqualityComparer<S>.Default.Equals(item1, item2);
        return ReferenceEquals(item1, item2);
    }

    public override bool Equals(object obj) { return obj is TimedEvent<T> @event && Compare(Value, @event.Value); }
    public override int GetHashCode() { return Value.GetHashCode(); }
    public override string ToString() {
        return Value + " T:" + eventTime;
    }
}

public static class Easing {

    public static float PowerEase(float from, float to, float power, float amount, float choke = 0) {
        float dAbs = (float)Math.Pow((float)Math.Abs(from - to) + choke, 1.0f / power);
        dAbs = Math.Max(dAbs - amount, 0);
        var newVal = to + (from < to ? -1 : 1) * Math.Max((float)Math.Pow(dAbs, power) - choke, 0);
        //if (newVal == from) newVal = to;
        return newVal;
    }
    public static Vector2 PowerEase(Vector2 from, Vector2 to, float power, float amount) {
        var delta = to - from;
        var deltaLen = delta.Length();
        if (deltaLen <= float.Epsilon) return to;
        var newLen = PowerEase(deltaLen, 0, power, amount);
        return to - delta * newLen / deltaLen;
    }
    public static Vector3 PowerEase(Vector3 from, Vector3 to, float power, float amount) {
        var delta = to - from;
        var deltaLen = delta.Length();
        if (deltaLen <= float.Epsilon) return to;
        var newLen = PowerEase(deltaLen, 0, power, amount);
        return to - delta * newLen / deltaLen;
    }

    private static float PowerIn(float from, float to, float power, float lerp) {
        if (lerp <= float.Epsilon) return from;
        if (lerp >= 0.99999f) return to;
        lerp = from + (to - from) * ((float)Math.Pow(lerp, power));
        Debug.Assert(!float.IsNaN(lerp), "NaN found");
        return lerp;
    }
    private static float BackOut(float from, float to, float amplitude, float lerp) {
        float t = 1 - lerp;
        return to + (from - to) * (t * t * t - t * amplitude * MathF.Sin(t * MathF.PI));
    }
    private static float BackIn(float from, float to, float amplitude, float lerp) {
        float t = lerp;
        return from + (to - from) * (t * t * t - t * amplitude * MathF.Sin(t * MathF.PI));
    }

    private static float BubbleEase2(float from, float to, float amplitude, float pulses, float lerp, float midN = 0.6f) {
        var mid = 1 / pulses * midN;
        return BubbleEase(
            Easing.PowerIn(from, Easing.Lerp(to, from, amplitude), 2, Easing.InverseLerp(0, mid, lerp)),
            to, 1, pulses, lerp);
    }

    public static float Lerp(float from, float to, float lerp) {
        return from + (to - from) * lerp;
    }
    public static float InverseLerp(float from, float to, float value) {
        return (value - from) / (to - from);
    }
    public static float Clamp01(float value) {
        return value < 0f ? 0f : value > 1f ? 1f : value;
    }

    private static float BubbleEase(float from, float to, float amplitude, float pulses, float lerp) {
        //float t = 1 - lerp;
        //return from + (to - from) * (1 - amplitude * t * t * Mathf.Sin(lerp * 3.14f * 5.0f));
        var a = lerp * pulses * (2 * (float)Math.PI);
        return to + (from - to) *
            MathF.Pow(amplitude / 2, 10 * lerp) *
            //(1 + Mathf.Cos(Mathf.Pow(lerp, amplitude) * Mathf.PI)) / 2 *
            MathF.Cos(a);
    }

    public interface IEaseFunction {
        float Duration { get; }
        float Evaluate(float time);
        float EvaluateLerp(float lerp);
    }
    public struct EPower : IEaseFunction {
        public float Power;
        public float Duration;
        float IEaseFunction.Duration { get { return Duration; } }
        public float Evaluate(float time) { return EvaluateLerp(Easing.Clamp01(time / Duration)); }
        public float EvaluateLerp(float lerp) { return MathF.Pow(lerp, Power); }
    }
    public struct EBack : IEaseFunction {
        public float Amplitude;
        public float Duration;
        float IEaseFunction.Duration { get { return Duration; } }
        public float Evaluate(float time) { return EvaluateLerp(Easing.Clamp01(time / Duration)); }
        public float EvaluateLerp(float lerp) { return BackIn(0f, 1f, Amplitude, lerp); }
    }
    public struct EBubble : IEaseFunction {
        public float Count;
        public float Amplitude;
        public float Duration;
        float IEaseFunction.Duration { get { return Duration; } }
        public float Evaluate(float time) { return EvaluateLerp(Easing.Clamp01(time / Duration)); }
        public float EvaluateLerp(float lerp) { return BubbleEase2(0f, 1f, Amplitude, Count, lerp); }
    }
    public struct EInOut<T> : IEaseFunction where T : IEaseFunction {
        public T Function;
        public float Duration { get { return Function.Duration; } }
        public float Evaluate(float time) { return EvaluateLerp(Easing.Clamp01(time / Function.Duration)); }
        public float EvaluateLerp(float lerp) {
            return lerp < 0.5f ? Function.EvaluateLerp(lerp / 0.5f) * 0.5f : 1f - Function.EvaluateLerp((1f - lerp) / 0.5f) * 0.5f;
        }
    }
    public struct EOut<T> : IEaseFunction where T : IEaseFunction {
        public T Function;
        public float Duration { get { return Function.Duration; } }
        public float Evaluate(float time) { return EvaluateLerp(Easing.Clamp01(time / Function.Duration)); }
        public float EvaluateLerp(float lerp) { return 1f - Function.EvaluateLerp(1f - lerp); }
    }
    public struct EReverse<T> where T : IEaseFunction {
        public T Function;
        public float Duration { get { return Function.Duration; } }
        public float Evaluate(float time) { return Function.Evaluate(Duration - time); }
    }
    public struct EFromTo<T> where T : IEaseFunction {
        public T Function;
        public float From, To;
        public float Duration { get { return Function.Duration; } }
        public float Evaluate(float time) { return From + (To - From) * Function.Evaluate(time); }

        public EFromTo<T> SwapFromTo(bool enable = true) {
            if (!enable) return this;
            return new EFromTo<T>() { Function = Function, From = To, To = From, };
        }
    }
    public struct EDelay<T> : IEaseFunction where T : IEaseFunction {
        public T Function;
        public float Delay;
        public float Duration { get { return Delay + Function.Duration; } }
        public float Evaluate(float time) { return Function.Evaluate(time - Delay); }
        public float EvaluateLerp(float lerp) { return Function.EvaluateLerp(lerp - Delay / Function.Duration); }
    }

    public struct EStateful<T> : IEaseFunction where T : IEaseFunction {
        public T Function;
        public float Duration { get { return Function.Duration; } }
        public float EvaluateL0(float time) { return EvaluateL0(time, TimedEvent.ClockDelta); }
        public float EvaluateL0(float time, float dt) { return EvaluateLerp(time - dt); }
        public float EvaluateL1(float time) { return EvaluateL1(time); }
        public float EvaluateL1(float time, float dt) { return EvaluateLerp(time); }
        public float EvaluateLerp(float time) { return Function.Evaluate(time); }
        public float Evaluate(float time) { return Evaluate(time, TimedEvent.ClockDelta); }
        public float Evaluate(float time, float dt) { return EvaluateT0T1(time - dt, time); }
        public float EvaluateT0T1(float t0, float t1) {
            if (t1 <= 0f) return 0f;
            if (t1 >= Duration) return 1f;
            var l0 = Function.Evaluate(t0);
            var l1 = Function.Evaluate(t1);
            return l0 != 1 ? (l1 - l0) / (1 - l0) : 1;
        }
        public EStateful<EDelay<T>> WithDelay(float delay) {
            return new() { Function = new EDelay<T>() { Function = Function, Delay = delay, } };
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EPower PowerIn(float duration, float power = 2f) {
        return new EPower() { Power = power, Duration = duration, };
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EOut<EPower> PowerOut(float duration, float power = 2f) {
        return new EOut<EPower>() { Function = PowerIn(duration, power), };
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EInOut<EPower> PowerInOut(float duration, float power = 2f) {
        return new EInOut<EPower>() { Function = PowerIn(duration, power), };
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EStateful<EPower> StatefulPowerIn(float duration, float power) {
        return MakeStateful(PowerIn(duration, power));
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EStateful<EOut<EPower>> StatefulPowerOut(float duration, float power) {
        return MakeStateful(PowerOut(duration, power));
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EStateful<EInOut<EPower>> StatefulPowerInOut(float duration, float power) {
        return MakeStateful(PowerInOut(duration, power));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EBack BackIn(float duration, float amplitude = 1f) {
        return new EBack() { Duration = duration, Amplitude = amplitude, };
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EOut<EBack> BackOut(float duration, float amplitude = 1f) {
        return new EOut<EBack>() { Function = BackIn(duration, amplitude), };
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EInOut<EBack> BackInOut(float duration, float amplitude = 1f) {
        return new EInOut<EBack>() { Function = BackIn(duration, amplitude), };
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EStateful<EBack> StatefulBackIn(float duration, float amplitude = 1f) {
        return MakeStateful(BackIn(duration, amplitude));
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EStateful<EOut<EBack>> StatefulBackOut(float duration, float amplitude = 1f) {
        return MakeStateful(BackOut(duration, amplitude));
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EStateful<EInOut<EBack>> StatefulBackInOut(float duration, float amplitude = 1f) {
        return MakeStateful(BackInOut(duration, amplitude));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EBubble BubbleIn(float duration, float amplitude = 1f, float count = 3f) {
        return new EBubble() { Duration = duration, Amplitude = amplitude, Count = count, };
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EOut<EBubble> BubbleOut(float duration, float amplitude = 1f, float count = 3f) {
        return new EOut<EBubble>() { Function = BubbleIn(duration, amplitude, count), };
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EInOut<EBubble> BubbleInOut(float duration, float amplitude = 1f, float count = 3f) {
        return new EInOut<EBubble>() { Function = BubbleIn(duration, amplitude, count), };
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EStateful<EBubble> StatefulBubbleIn(float duration, float amplitude = 1f, float count = 3f) {
        return MakeStateful(BubbleIn(duration, amplitude, count));
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EStateful<EOut<EBubble>> StatefulBubbleOut(float duration, float amplitude = 1f, float count = 3f) {
        return MakeStateful(BubbleOut(duration, amplitude, count));
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EStateful<EInOut<EBubble>> StatefulBubbleInOut(float duration, float amplitude = 1f, float count = 3f) {
        return MakeStateful(BubbleInOut(duration, amplitude, count));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EStateful<Fn> MakeStateful<Fn>(this Fn fn) where Fn : IEaseFunction {
        return new EStateful<Fn>() { Function = fn, };
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EReverse<Fn> MakeReverse<Fn>(this Fn fn) where Fn : IEaseFunction {
        return new EReverse<Fn>() { Function = fn, };
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EFromTo<Fn> WithFromTo<Fn>(this Fn fn, float from = 0f, float to = 1f) where Fn : IEaseFunction {
        return new EFromTo<Fn>() { Function = fn, From = from, To = to, };
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EDelay<Fn> WithDelay<Fn>(this Fn fn, float delay) where Fn : IEaseFunction {
        return new EDelay<Fn>() { Function = fn, Delay = delay, };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetIsComplete<Fn, Timed>(this EStateful<Fn> fn, Timed tevent, float delay = 0f) where Fn : IEaseFunction where Timed : ITimedEvent {
        return tevent.TimeSinceEvent - delay - TimedEvent.ClockDelta > fn.Duration;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Evaluate<Fn, Timed>(this EStateful<Fn> fn, Timed tevent, float delay = 0f) where Fn : IEaseFunction where Timed : ITimedEvent {
        return fn.Evaluate(tevent.TimeSinceEvent - delay, TimedEvent.ClockDelta);
    }
}