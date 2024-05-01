using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace nldksample;

/// <summary>
/// Represents a thread-safe collection of key/value pairs that can be accessed by multiple threads concurrently and provides
/// notifications when items get added, removed, or when the whole list is refreshed.
/// </summary>
/// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
/// <typeparam name="TValue">The type of the values in the dictionary.</typeparam>
public sealed class ObservableConcurrentDictionary<TKey, TValue> : ConcurrentDictionary<TKey, TValue>, INotifyCollectionChanged, INotifyPropertyChanged
{
   private const string IndexerName = "Item[]";

   /// <summary>
   /// Initializes a new instance of the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> class that is empty, has the
   /// default concurrency level, has the default initial capacity, and uses the default comparer for the key type.
   /// </summary>
   /// <remarks>
   /// The default concurrency level is equal to the number of CPUs. The higher the concurrency level is, the more concurrent write
   /// operations can take place without interference and blocking. Higher concurrency level values also cause operations that require
   /// all locks (for example, table resizing, ToArray and Count) to become more expensive. The default capacity (DEFAULT_CAPACITY),
   /// which represents the initial number of buckets, is a trade-off between the size of a very small dictionary and the number of
   /// resizes when constructing a large dictionary. Also, the capacity should not be divisible by a small prime number. The default
   /// capacity is 31.
   /// </remarks>
   public ObservableConcurrentDictionary() : base()
   {
   }

   /// <summary>Initializes a new instance of the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> class that contains
   /// elements copied from the specified IEnumerable<T>, has the default concurrency level, has the default initial capacity, and uses
   /// the default comparer for the key type.</summary> <param name="collection">The <see cref="IEnumerable{T}" /> whose elements are
   /// copied to the new <see cref="ObservableConcurrentDictionary{TKey, TValue}" />.</param>
   public ObservableConcurrentDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection) : base(collection)
   {
   }

   /// <summary>
   /// Initializes a new instance of the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> class that is empty, has the
   /// default concurrency level and capacity, and uses the specified <see cref="IEqualityComparer{T}"/>.
   /// </summary>
   /// <param name="comparer">The equality comparison implementation to use when comparing keys.</param>
   public ObservableConcurrentDictionary(IEqualityComparer<TKey> comparer) : base(comparer)
   {
   }

   /// <summary>
   /// Initializes a new instance of the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> class that is empty, has the
   /// specified concurrency level and capacity, and uses the default comparer for the key type.
   /// </summary>
   /// <param name="concurrencyLevel">
   /// The estimated number of threads that will update the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> concurrently.
   /// </param>
   /// <param name="capacity">
   /// The initial number of elements that the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> can contain.
   /// </param>
   public ObservableConcurrentDictionary(int concurrencyLevel, int capacity) : base(concurrencyLevel, capacity)
   {
   }

   /// <summary>
   /// Initializes a new instance of the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> class that contains elements copied
   /// from the specified <see cref="IEnumerable{T}"/> has the default concurrency level, has the default initial capacity, and uses
   /// the specified <see cref="IEqualityComparer{T}"/>.
   /// </summary>
   /// <param name="collection">
   /// The <see cref="IEnumerable{T}"/> whose elements are copied to the new <see cref="ObservableConcurrentDictionary{TKey, TValue}"/>.
   /// </param>
   /// <param name="comparer">The <see cref="IEqualityComparer{T}"/> implementation to use when comparing keys.</param>
   public ObservableConcurrentDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer)
      : base(collection, comparer)
   {
   }

   /// <summary>
   /// Initializes a new instance of the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> class that is empty, has the
   /// specified concurrency level, has the specified initial capacity, and uses the specified <see cref="IEqualityComparer{T}"/>.
   /// </summary>
   /// <param name="concurrencyLevel">
   /// The estimated number of threads that will update the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> concurrently.
   /// </param>
   /// <param name="capacity">
   /// The initial number of elements that the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> can contain.
   /// </param>
   /// <param name="comparer">The <see cref="IEqualityComparer{T}"/> implementation to use when comparing keys.</param>
   public ObservableConcurrentDictionary(int concurrencyLevel, int capacity, IEqualityComparer<TKey> comparer)
      : base(concurrencyLevel, capacity, comparer)
   {
   }

   /// <summary>
   /// Initializes a new instance of the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> class that contains elements copied
   /// from the specified IEnumerable, and uses the specified <see cref="IEqualityComparer{T}"/>.
   /// </summary>
   /// <param name="concurrencyLevel">
   /// The estimated number of threads that will update the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> concurrently.
   /// </param>
   /// <param name="collection">
   /// The <see cref="IEnumerable{T}"/> whose elements are copied to the new <see cref="ObservableConcurrentDictionary{TKey, TValue}"/>.
   /// </param>
   /// <param name="comparer">The <see cref="IEqualityComparer{T}"/> implementation to use when comparing keys.</param>
   public ObservableConcurrentDictionary(int concurrencyLevel, IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer)
      : base(concurrencyLevel, collection, comparer)
   {
   }

   /// <summary>Occurs when an item is added, removed, changed, moved, or the entire list is refreshed.</summary>
   public event NotifyCollectionChangedEventHandler CollectionChanged;

   /// <summary>Occurs when a property value changes.</summary>
   public event PropertyChangedEventHandler PropertyChanged;

   /// <summary>
   /// Uses the specified functions to add a key/value pair to the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> if the
   /// key does not already exist, or to update a key/value pair in the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> if
   /// the key already exists.
   /// </summary>
   /// <param name="key">The key to be added or whose value should be updated</param>
   /// <param name="addValueFactory">The function used to generate a value for an absent key</param>
   /// <param name="updateValueFactory">
   /// The function used to generate a new value for an existing key based on the key's existing value
   /// </param>
   /// <returns>
   /// The new value for the key. This will be either be the result of <paramref name="addValueFactory"/> (if the key was absent) or
   /// the result of <paramref name="updateValueFactory"/> (if the key was present).
   /// </returns>
   public new TValue AddOrUpdate(TKey key, Func<TKey, TValue> addValueFactory, Func<TKey, TValue, TValue> updateValueFactory)
   {
      var add = !ContainsKey(key);
      var value = base.AddOrUpdate(key, addValueFactory, updateValueFactory);
      OnCollectionChanged(new NotifyCollectionChangedEventArgs(add ? NotifyCollectionChangedAction.Add : NotifyCollectionChangedAction.Replace, value));
      return value;
   }

   /// <summary>
   /// Adds a key/value pair to the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> if the key does not already exist, or
   /// updates a key/value pair in the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> by using the specified function if
   /// the key already exists.
   /// </summary>
   /// <param name="key">The key to be added or whose value should be updated</param>
   /// <param name="addValue">The value to be added for an absent key</param>
   /// <param name="updateValueFactory">
   /// The function used to generate a new value for an existing key based on the key's existing value
   /// </param>
   /// <returns>
   /// The new value for the key. This will be either be <paramref name="addValue"/> (if the key was absent) or the result of <paramref
   /// name="updateValueFactory"/> (if the key was present).
   /// </returns>
   public new TValue AddOrUpdate(TKey key, TValue addValue, Func<TKey, TValue, TValue> updateValueFactory)
   {
      var add = !ContainsKey(key);
      var value = base.AddOrUpdate(key, addValue, updateValueFactory);
      OnCollectionChanged(new NotifyCollectionChangedEventArgs(add ? NotifyCollectionChangedAction.Add : NotifyCollectionChangedAction.Replace, value));
      return value;
   }

#if !(NET40 || NET45 || NET451 || NET452 || NET46 || NET461 || NET462)
   /// <summary>
   /// Uses the specified functions and argument to add a key/value pair to the <see cref="ObservableConcurrentDictionary{TKey,
   /// TValue}"/> if the key does not already exist, or to update a key/value pair in the <see
   /// cref="ObservableConcurrentDictionary{TKey, TValue}"/> if the key already exists.
   /// </summary>
   /// <typeparam name="TArg">The type of an argument to pass into <paramref name="addValueFactory"/> and <paramref name="updateValueFactory"/>.</typeparam>
   /// <param name="key">The key to be added or whose value should be updated.</param>
   /// <param name="addValueFactory">The function used to generate a value for an absent key.</param>
   /// <param name="updateValueFactory">
   /// The function used to generate a new value for an existing key based on the key's existing value.
   /// </param>
   /// <param name="factoryArgument">An argument to pass into <paramref name="addValueFactory"/> and <paramref name="updateValueFactory"/>.</param>
   /// <returns>
   /// The new value for the key. This will be either be the result of <paramref name="addValueFactory"/> (if the key was absent) or
   /// the result of <paramref name="updateValueFactory"/> (if the key was present).
   /// </returns>
   public new TValue AddOrUpdate<TArg>(TKey key, Func<TKey, TArg, TValue> addValueFactory, Func<TKey, TValue, TArg, TValue> updateValueFactory, TArg factoryArgument)
   {
      var add = !ContainsKey(key);
      var value = base.AddOrUpdate(key, addValueFactory, updateValueFactory, factoryArgument);
      OnCollectionChanged(new NotifyCollectionChangedEventArgs(add ? NotifyCollectionChangedAction.Add : NotifyCollectionChangedAction.Replace, value));
      return value;
   }
#endif

   /// <summary>Removes all keys and values from the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/>.</summary>
   public new void Clear()
   {
      base.Clear();
      OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
   }

   /// <summary>
   /// Adds a key/value pair to the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> by using the specified function if the
   /// key does not already exist. Returns the new value, or the existing value if the key exists.
   /// </summary>
   /// <param name="key">The key of the element to add.</param>
   /// <param name="valueFactory">The function used to generate a value for the key.</param>
   /// <returns>
   /// The value for the key. This will be either the existing value for the key if the key is already in the dictionary, or the new
   /// value if the key was not in the dictionary.
   /// </returns>
   public new TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
   {
      var add = !ContainsKey(key);
      var value = base.GetOrAdd(key, valueFactory);
      if (add) OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value));
      return value;
   }

   /// <summary>
   /// Adds a key/value pair to the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> if the key does not already exist.
   /// Returns the new value, or the existing value if the key exists.
   /// </summary>
   /// <param name="key">The key of the element to add.</param>
   /// <param name="value">The value to be added, if the key does not already exist.</param>
   /// <returns>
   /// The value for the key. This will be either the existing value for the key if the key is already in the dictionary, or the new
   /// value if the key was not in the dictionary.
   /// </returns>
   public new TValue GetOrAdd(TKey key, TValue value)
   {
      var add = !ContainsKey(key);
      base.GetOrAdd(key, value);
      if (add) OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value));
      return value;
   }

#if !(NET40 || NET45 || NET451 || NET452 || NET46 || NET461 || NET462)
   /// <summary>
   /// Adds a key/value pair to the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/> by using the specified function and an
   /// argument if the key does not already exist, or returns the existing value if the key exists.
   /// </summary>
   /// <typeparam name="TArg">The type of an argument to pass into <paramref name="valueFactory"/>.</typeparam>
   /// <param name="key">The key of the element to add.</param>
   /// <param name="valueFactory">The function used to generate a value for the key.</param>
   /// <param name="factoryArgument">An argument value to pass into <paramref name="valueFactory"/>.</param>
   /// <returns>
   /// The value for the key. This will be either the existing value for the key if the key is already in the dictionary, or the new
   /// value if the key was not in the dictionary.
   /// </returns>
   public new TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TArg factoryArgument)
   {
      var add = !ContainsKey(key);
      var value = base.GetOrAdd(key, valueFactory, factoryArgument);
      if (add) OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value));
      return value;
   }
#endif

   /// <summary>Attempts to add the specified key and value to the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/>.</summary>
   /// <param name="key">The key of the element to add.</param>
   /// <param name="value">The value of the element to add. The value can be <see langword="null"/> for reference types.</param>
   /// <returns>
   /// <see langword="true"/> if the key/value pair was added to the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/>
   /// successfully; <see langword="false"/> if the key already exists.
   /// </returns>
   public new bool TryAdd(TKey key, TValue value)
   {
      if (base.TryAdd(key, value))
      {
         OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value));
         return true;
      }
      return false;
   }

   /// <summary>
   /// Attempts to remove and return the value that has the specified key from the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/>.
   /// </summary>
   /// <param name="key">The key of the element to remove and return.</param>
   /// <param name="value">
   /// When this method returns, contains the object removed from the <see cref="ObservableConcurrentDictionary{TKey, TValue}"/>, or
   /// the default value of the <typeparamref name="TValue"/> type if <paramref name="key"/> does not exist.
   /// </param>
   /// <returns><see langword="true"/> if the object was removed successfully; otherwise, <see langword="false"/>.</returns>
   public new bool TryRemove(TKey key, out TValue value)
   {
      if (base.TryRemove(key, out value))
      {
         OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, value));
         return true;
      }
      return false;
   }

   /// <summary>
   /// Updates the value associated with <paramref name="key"/> to <paramref name="newValue"/> if the existing value with <paramref
   /// name="key"/> is equal to <paramref name="comparisonValue"/>.
   /// </summary>
   /// <param name="key">The key of the value that is compared with <paramref name="comparisonValue"/> and possibly replaced.</param>
   /// <param name="newValue">
   /// The value that replaces the value of the element that has the specified <paramref name="key"/> if the comparison results in equality.
   /// </param>
   /// <param name="comparisonValue">The value that is compared with the value of the element that has the specified <paramref name="key"/>.</param>
   /// <returns>
   /// <see langword="true"/> if the value with <paramref name="key"/> was equal to <paramref name="comparisonValue"/> and was replaced
   /// with <paramref name="newValue"/>; otherwise, <see langword="false"/>.
   /// </returns>
   public new bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue)
   {
      if (base.TryUpdate(key, newValue, comparisonValue))
      {
         OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newValue));
         return true;
      }
      return false;
   }

   /// <summary>Raises the <see cref="CollectionChanged"/> event.</summary>
   /// <param name="e">The <see cref="NotifyCollectionChangedEventArgs"/> instance containing the event data.</param>
   private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
   {
      if (e.Action != NotifyCollectionChangedAction.Replace) OnPropertyChanged(nameof(Count));
      OnPropertyChanged(IndexerName);
      CollectionChanged?.Invoke(this, e);
   }

   /// <summary>Raises the <see cref="PropertyChanged" /> event.</summary>
   /// <param name="propertyName">Name of the property that has changed.</param>
   private void OnPropertyChanged([CallerMemberName] string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}