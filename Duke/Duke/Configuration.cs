﻿using System;
using System.Collections.Generic;
using Duke.Comparators;
using Duke.Utils;
using NLog;

namespace Duke
{
    /// <summary>
    /// Holds the configuration details for a dataset.
    /// </summary>
    public class Configuration
    {
        #region Private member variables

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();


        // there are two modes: deduplication and record linkage. in
        // deduplication mode all sources are in 'datasources'. in record
        // linkage mode they are in 'group1' and 'group2'. couldn't think
        // of a better solution. sorry.
        private readonly List<IDataSource> _datasources;
        private readonly DatabaseProperties _dbprops;
        private readonly List<IDataSource> _group1;
        private readonly List<IDataSource> _group2;
        private List<Property> _lookups; // subset of properties

        private Dictionary<String, Property> _properties;
        private List<Property> _proplist; // duplicate to preserve order

        #endregion

        #region Member Properties

        /// The probability threshold used to decide whether two records
        /// represent the same entity. If the probability is higher than this
        /// value, the two records are considered to represent the same entity.
        public double Threshold { get; set; }

        /// The probability threshold used to decide whether two records may
        /// represent the same entity. If the probability is higher than this
        /// value, the two records are considered possible matches. Can be 0,
        /// in which case no records are considered possible matches.
        public double ThresholdMaybe { get; set; }

        /// Returns the path to the Lucene index directory. If null, it means
        /// the Lucene index is kept in-memory.
        public string Path { get; set; }

        #endregion

        #region Constructors

        public Configuration()
        {
            logger.Debug("Intializing Configuration");
            _datasources = new List<IDataSource>();
            _group1 = new List<IDataSource>();
            _group2 = new List<IDataSource>();
            _dbprops = new DatabaseProperties();
        }

        #endregion

        #region Member methods

        /// <summary>
        /// Returns the data sources to use (in deduplication mode; don't use
        /// this method in record linkage mode).
        /// </summary>
        /// <returns></returns>
        public List<IDataSource> GetDataSources()
        {
            return _datasources;
        }

        /// <summary>
        /// Returns the data sources belonging to a particular group of data
        /// sources. Data sources are grouped in record linkage mode, but not
        /// in deduplication mode, so only use this method in record linkage
        /// mode.
        /// </summary>
        /// <param name="groupno"></param>
        /// <returns></returns>
        public List<IDataSource> GetDataSources(int groupno)
        {
            if (groupno == 1)
            {
                return _group1;
            }

            if (groupno == 2)
            {
                return _group2;
            }

            throw new Exception(String.Format("Invalid group number: {0}", groupno));
        }

        /// <summary>
        /// Adds a data source to the configuration. If in deduplication mode
        /// groupno == 0, otherwise it gives the number of the group to which
        /// the data source belongs.
        /// </summary>
        /// <param name="groupno"></param>
        /// <param name="datasource"></param>
        public void AddDataSource(int groupno, IDataSource datasource)
        {
            logger.Debug("New datasource added for groupno {0}: {1}", groupno, datasource.ToString());
            // the load takes care of validation
            if (groupno == 0)
            {
                _datasources.Add(datasource);
            }
            if (groupno == 1)
            {
                _group1.Add(datasource);
            }
            if (groupno == 2)
            {
                _group2.Add(datasource);
            }
        }

        /// <summary>
        /// FIXME: means we can create multiple ones. not a good idea.
        /// </summary>
        /// <param name="overwrite"></param>
        /// <returns></returns>
        public IDatabase CreateDatabase(Boolean overwrite)
        {
            if (_dbprops.GetDatabaseImplementation() ==
                DatabaseImplementation.IN_MEMORY_DATABASE)
            {
                return new InMemoryDatabase(this);
            }
            return new LuceneDatabase(this, overwrite, _dbprops);
        }

        /// <summary>
        /// Returns whether we are in deduplication mode
        /// </summary>
        /// <returns>true if we are in deduplication mode</returns>
        public bool IsDeduplicationMode()
        {
            return !(GetDataSources().Count == 0 || GetDataSources() == null);
        }

        public void SetProperties(List<Property> props)
        {
            _proplist = props;
            _properties = new Dictionary<string, Property>(props.Count);
            foreach (Property property in props)
            {
                _properties.Add(property.Name, property);
            }

            // analyze properties to find lookup set
            //FindLookupProperties();

            //TODO: Uncomment the find above
        }

        /// <summary>
        /// The set of properties Duke records can have, and their associated
        /// cleaners, comparators, and probabilities.
        /// </summary>
        /// <returns></returns>
        public List<Property> GetProperties()
        {
            return _proplist;
        }

        /// <summary>
        /// The properties which are used to identify records, rather than
        /// compare them.
        /// </summary>
        /// <returns></returns>
        public List<Property> GetIdentityProperties()
        {
            var ids = new List<Property>();
            foreach (Property property in GetProperties())
            {
                if (property.IsIdProperty)
                {
                    ids.Add(property);
                }
            }

            return ids;
        }

        /// <summary>
        ///  Returns the property with the given name, or null if there is no
        ///  such property.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public Property GetPropertyByName(String name)
        {
            return _properties[name];
        }

        /// <summary>
        /// Returns the db props.
        /// </summary>
        /// <returns></returns>
        public DatabaseProperties GetDatabaseProperties()
        {
            return _dbprops;
        }

        /// <summary>
        /// Returns the properties Duke queries for in the Lucene index. 
        /// This is a subset of getProperties(), and is computed based on 
        /// the probabilities and the threshold.
        /// </summary>
        /// <returns></returns>
        public List<Property> GetLookupProperties()
        {
            return _lookups;
        }

        private void FindLookupProperties()
        {
            var candidates = new List<Property>();
            foreach (Property property in _properties.Values)
            {
                if (!property.IsIdProperty || property.IsIgnoreProperty())
                {
                    candidates.Add(property);
                }
            }

            candidates.Sort(HighComparator.Compare);
            //TODO: see if the HighComparator even needs to be a separate class...

            int last = -1;
            double prob = 0.5;
            double limit = ThresholdMaybe;
            if (limit == 0.0)
                limit = Threshold;

            for (int ix = 0; ix < candidates.Count; ix++)
            {
                Property prop = candidates[ix];
                if (prop.HighProbability == 0.0)
                    // if the probability is zero we ignore the property entirely
                    continue;

                prob = StandardUtils.ComputeBayes(prob, prop.HighProbability);
                if (prob >= Threshold)
                {
                    if (last == -1)
                        last = ix;
                    break;
                }
                if (prob >= limit && last == -1)
                    last = ix;
            }

            if (prob < Threshold)
                //throw new DukeConfigException("Maximum possible probability is " + prob +
                //                           ", which is below threshold (" + threshold +
                //                           "), which means no duplicates will ever " +
                //                           "be found");
                throw new Exception(String.Format("Maximum possible probability is {0}, which is below threshold ({1}" +
                                                  "), which means no duplicates will ever be found", prob, Threshold));
            if (last == -1)
                _lookups.Clear();
            else
                _lookups = new List<Property>(candidates.GetRange(last, candidates.Count));
        }

        #endregion
    }
}