﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Qualities
{
    public interface IQualityProfileRepository : IBasicRepository<QualityProfile>
    {
        
    }

    public class QualityProfileRepository : BasicRepository<QualityProfile>, IQualityProfileRepository
    {
        public QualityProfileRepository(IObjectDatabase database)
                : base(database)
        {
        }
    }
}
