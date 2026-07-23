using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// サービスの登録・取得を行うクラス。
/// シーン遷移のたびにClearを呼ぶこと。
/// </summary>
public static class ServiceLocator
{
    static private readonly Dictionary<Type, object> services_ = new();

    /// <summary>サービスを登録</summary>
    public static void Register<T>(T service) where T : class
    {
        services_[typeof(T)] = service;
    }

    /// <summary>サービスを取得する。未登録の場合は例外を投げる</summary>
    public static T Get<T>() where T : class
    {
        if (services_.TryGetValue(typeof(T), out var service))
            return service as T;

        throw new InvalidOperationException(
            $"[ServiceLocator] {typeof(T).Name} が未登録です。" +
            $"Register<{typeof(T).Name}>() を先に呼んでください。");
    }

    /// <summary>サービスを取得する。未登録の場合はnullを返す</summary>
    public static T TryGet<T>() where T : class
    {
        services_.TryGetValue(typeof(T), out var service);
        return service as T;
    }

    /// <summary>シーン遷移時に全サービスをクリアする</summary>
    public static void Clear()
    {
        services_.Clear();
    }
}