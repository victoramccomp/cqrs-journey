﻿// ==============================================================================================================
// Microsoft patterns & practices
// CQRS Journey project
// ==============================================================================================================
// ©2012 Microsoft. All rights reserved. Certain content used with permission from contributors
// http://cqrsjourney.github.com/contributors/members
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance 
// with the License. You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is 
// distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
// See the License for the specific language governing permissions and limitations under the License.
// ==============================================================================================================

namespace Registration.Handlers
{
    using System;
    using System.Collections.Generic;
    using System.Data.Entity;
    using System.Diagnostics;
    using System.Linq;
    using Conference;
    using Infrastructure.Messaging.Handling;
    using Registration.Events;
    using Registration.ReadModel;
    using Registration.ReadModel.Implementation;

    public class PricedOrderViewModelGenerator :
        IEventHandler<OrderTotalsCalculated>,
        IEventHandler<OrderExpired>,
        IEventHandler<SeatAssignmentsCreated>,
        IEventHandler<SeatCreated>,
        IEventHandler<SeatUpdated>
    {
        private readonly Func<ConferenceRegistrationDbContext> contextFactory;

        public PricedOrderViewModelGenerator(Func<ConferenceRegistrationDbContext> contextFactory)
        {
            this.contextFactory = contextFactory;
        }

        public void Handle(OrderTotalsCalculated @event)
        {
            var seatTypeIds = @event.Lines.OfType<SeatOrderLine>().Select(x => x.SeatType).Distinct().ToArray();
            using (var context = this.contextFactory.Invoke())
            {
                var dto = context.Query<PricedOrder>().Include(x => x.Lines).FirstOrDefault(x => x.OrderId == @event.SourceId);
                if (dto == null)
                {
                    dto = new PricedOrder { OrderId = @event.SourceId };
                    context.Set<PricedOrder>().Add(dto);
                }
                else
                {
                    var linesSet = context.Set<PricedOrderLine>();
                    foreach (var line in dto.Lines.ToList())
                    {
                        linesSet.Remove(line);
                    }
                }

                List<PricedOrderLineSeatTypeDescription> seatTypeDescriptions;
                if (seatTypeIds.Length != 0)
                {
                    seatTypeDescriptions = context.Query<PricedOrderLineSeatTypeDescription>()
                        .Where(x => seatTypeIds.Contains(x.SeatTypeId))
                        .ToList();
                }
                else
                {
                    seatTypeDescriptions = new List<PricedOrderLineSeatTypeDescription>();
                }

                foreach (var orderLine in @event.Lines)
                {
                    var line = new PricedOrderLine
                    {
                        Position = dto.Lines.Count,
                        LineTotal = orderLine.LineTotal,
                    };

                    var seatOrderLine = orderLine as SeatOrderLine;
                    if (seatOrderLine != null)
                    {
                        // should we update the view model to avoid loosing the SeatTypeId?
                        line.Description = seatTypeDescriptions.Where(x => x.SeatTypeId == seatOrderLine.SeatType).Select(x => x.Name).FirstOrDefault();
                        line.UnitPrice = seatOrderLine.UnitPrice;
                        line.Quantity = seatOrderLine.Quantity;
                    }

                    dto.Lines.Add(line);
                }

                dto.Total = @event.Total;
                dto.IsFreeOfCharge = @event.IsFreeOfCharge;
                dto.OrderVersion = @event.Version;

                context.SaveChanges();
            }
        }

        public void Handle(OrderExpired @event)
        {
            // No need to keep this priced order alive if it is expired.
            using (var context = this.contextFactory.Invoke())
            {
                var dto = context.Find<PricedOrder>(@event.SourceId);
                if (dto != null)
                {
                    context.Set<PricedOrder>().Remove(dto);
                    context.SaveChanges();
                }
                else
                {
                    Trace.TraceError("Failed to locate Priced order corresponding to the expired order with id {0}.", @event.SourceId);
                }
            }
        }

        /// <summary>
        /// Saves the seat assignments correlation ID for further lookup.
        /// </summary>
        public void Handle(SeatAssignmentsCreated @event)
        {
            using (var context = this.contextFactory.Invoke())
            {
                var dto = context.Find<PricedOrder>(@event.OrderId);
                if (dto != null)
                {
                    dto.AssignmentsId = @event.SourceId;
                    context.SaveChanges();
                }
                else
                {
                    Trace.TraceError("Failed to locate Priced order corresponding to the seat assignments created, order  with id {0}.", @event.OrderId);
                }
            }
        }

        public void Handle(SeatCreated @event)
        {
            using (var context = this.contextFactory.Invoke())
            {
                var dto = context.Find<PricedOrderLineSeatTypeDescription>(@event.SourceId);
                if (dto == null)
                {
                    dto = new PricedOrderLineSeatTypeDescription { SeatTypeId = @event.SourceId };
                    context.Set<PricedOrderLineSeatTypeDescription>().Add(dto);
                }

                dto.Name = @event.Name;
                context.SaveChanges();
            }
        }

        public void Handle(SeatUpdated @event)
        {
            using (var context = this.contextFactory.Invoke())
            {
                var dto = context.Find<PricedOrderLineSeatTypeDescription>(@event.SourceId);
                if (dto == null)
                {
                    dto = new PricedOrderLineSeatTypeDescription { SeatTypeId = @event.SourceId };
                    context.Set<PricedOrderLineSeatTypeDescription>().Add(dto);
                }

                dto.Name = @event.Name;
                context.SaveChanges();
            }
        }
    }
}
